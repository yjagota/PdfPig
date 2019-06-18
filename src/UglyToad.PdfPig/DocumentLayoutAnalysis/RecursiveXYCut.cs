﻿using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Geometry;

namespace UglyToad.PdfPig.DocumentLayoutAnalysis
{
    /// <summary>
    /// The recursive X-Y cut is a top-down page segmentation technique that decomposes a document 
    /// recursively into a set of rectangular blocks. This implementation leverages bounding boxes.
    /// https://en.wikipedia.org/wiki/Recursive_X-Y_cut
    /// <para>See 'Recursive X-Y Cut using Bounding Boxes of Connected Components' by Jaekyu Ha, Robert M.Haralick and Ihsin T. Phillips</para>
    /// </summary>
    public class RecursiveXYCut
    {
        /// <summary>
        /// Get the blocks.
        /// </summary>
        /// <param name="pageWords">The words in the page.</param>
        /// <param name="minimumWidth">The minimum width for a block.</param>
        /// <param name="dominantFontWidth">The dominant font width.</param>
        /// <param name="dominantFontHeight">The dominant font height.</param>
        /// <returns></returns>
        public static XYNode GetBlocks(IEnumerable<Word> pageWords, decimal minimumWidth,
            decimal dominantFontWidth, decimal dominantFontHeight)
        {
            return GetBlocks(pageWords, minimumWidth, k => dominantFontWidth, k => dominantFontHeight);
        }

        /// <summary>
        /// Get the blocks.
        /// </summary>
        /// <param name="pageWords">The words in the page.</param>
        /// <param name="minimumWidth">The minimum width for a block.</param>
        /// <param name="dominantFontWidthFunc">The function that determines the dominant font width.</param>
        /// <param name="dominantFontHeightFunc">The function that determines the dominant font height.</param>
        /// <returns></returns>
        public static XYNode GetBlocks(IEnumerable<Word> pageWords, decimal minimumWidth,
            Func<IEnumerable<decimal>, decimal> dominantFontWidthFunc,
            Func<IEnumerable<decimal>, decimal> dominantFontHeightFunc)
        {
            var root = new XYLeaf(pageWords);
            return VerticalCut(root, minimumWidth, dominantFontWidthFunc, dominantFontHeightFunc);
        }

        private static XYNode VerticalCut(XYLeaf leaf, decimal minimumWidth,
            Func<IEnumerable<decimal>, decimal> dominantFontWidthFunc,
            Func<IEnumerable<decimal>, decimal> dominantFontHeightFunc, int level = 0)
        {
            if (leaf.CountWords() <= 1 || leaf.BoundingBox.Width <= minimumWidth)
            {
                // we stop cutting if 
                // - only one word remains
                // - width is too small
                return leaf;
            }

            // order words left to right
            var words = leaf.Words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).OrderBy(w => w.BoundingBox.Left).ToArray();

            // determine dominantFontWidth and dominantFontHeight
            decimal domFontWidth = dominantFontWidthFunc(words.SelectMany(x => x.Letters)
                .Select(x => Math.Abs(x.GlyphRectangle.Width)));
            decimal domFontHeight = dominantFontHeightFunc(words.SelectMany(x => x.Letters)
                .Select(x => Math.Abs(x.GlyphRectangle.Height)));

            List<decimal[]> projectionProfile = new List<decimal[]>();
            decimal[] currentProj = new decimal[2] { words[0].BoundingBox.Left, words[0].BoundingBox.Right };
            int wordsCount = words.Count();
            for (int i = 1; i < wordsCount; i++)
            {
                if ((words[i].BoundingBox.Left >= currentProj[0] && words[i].BoundingBox.Left <= currentProj[1])
                    || (words[i].BoundingBox.Right >= currentProj[0] && words[i].BoundingBox.Right <= currentProj[1]))
                {
                    // it is overlapping 
                    if (words[i].BoundingBox.Left >= currentProj[0]
                        && words[i].BoundingBox.Left <= currentProj[1]
                        && words[i].BoundingBox.Right > currentProj[1])
                    {
                        // |____|
                        //    |____|
                        // |_______|    <- updated
                        currentProj[1] = words[i].BoundingBox.Right;
                    }

                    // we ignore the following cases:
                    //    |____|
                    // |____|          (not possible because of OrderBy)
                    // 
                    //    |____|
                    //|___________|    (not possible because of OrderBy)
                    //
                    //  |____|
                    //   |_|
                }
                else
                {
                    // no overlap
                    if (words[i].BoundingBox.Left - currentProj[1] <= domFontWidth)
                    {
                        // if gap too small -> don't cut
                        // |____| |____|
                        currentProj[1] = words[i].BoundingBox.Right;
                    }
                    else if (currentProj[1] - currentProj[0] < minimumWidth)
                    {
                        // still too small
                        currentProj[1] = words[i].BoundingBox.Right;
                    }
                    else
                    {
                        // if gap big enough -> cut!
                        // |____|   |   |____|
                        if (i != wordsCount - 1) // will always add the last one after
                        {
                            projectionProfile.Add(currentProj);
                            currentProj = new decimal[2] { words[i].BoundingBox.Left, words[i].BoundingBox.Right };
                        }
                    }
                }
                if (i == wordsCount - 1) projectionProfile.Add(currentProj);
            }

            var newLeafsEnums = projectionProfile.Select(p => leaf.Words.Where(w => w.BoundingBox.Left >= p[0] && w.BoundingBox.Right <= p[1]));
            var newLeafs = newLeafsEnums.Where(e => e.Count() > 0).Select(e => new XYLeaf(e));

            var newNodes = newLeafs.Select(l => HorizontalCut(l, minimumWidth,
                dominantFontWidthFunc, dominantFontHeightFunc, level)).ToList();

            var lost = leaf.Words.Except(newLeafsEnums.SelectMany(x => x)).Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
            if (lost.Count > 0)
            {
                newNodes.AddRange(lost.Select(w => new XYLeaf(w)));
            }

            return new XYNode(newNodes);
        }

        private static XYNode HorizontalCut(XYLeaf leaf, decimal minimumWidth,
            Func<IEnumerable<decimal>, decimal> dominantFontWidthFunc,
            Func<IEnumerable<decimal>, decimal> dominantFontHeightFunc, int level = 0)
        {
            if (leaf.CountWords() <= 1)
            {
                // we stop cutting if 
                // - only one word remains
                return leaf;
            }

            var words = leaf.Words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).OrderBy(w => w.BoundingBox.Bottom).ToArray(); // order bottom to top

            // determine dominantFontWidth and dominantFontHeight
            decimal domFontWidth = dominantFontWidthFunc(words.SelectMany(x => x.Letters)
                .Select(x => Math.Abs(x.GlyphRectangle.Width)));
            decimal domFontHeight = dominantFontHeightFunc(words.SelectMany(x => x.Letters)
                .Select(x => Math.Abs(x.GlyphRectangle.Height)));

            List<decimal[]> projectionProfile = new List<decimal[]>();
            decimal[] currentProj = new decimal[2] { words[0].BoundingBox.Bottom, words[0].BoundingBox.Top };
            int wordsCount = words.Count();
            for (int i = 1; i < wordsCount; i++)
            {
                if ((words[i].BoundingBox.Bottom >= currentProj[0] && words[i].BoundingBox.Bottom <= currentProj[1])
                    || (words[i].BoundingBox.Top >= currentProj[0] && words[i].BoundingBox.Top <= currentProj[1]))
                {
                    // it is overlapping 
                    if (words[i].BoundingBox.Bottom >= currentProj[0]
                        && words[i].BoundingBox.Bottom <= currentProj[1]
                        && words[i].BoundingBox.Top > currentProj[1])
                    {
                        currentProj[1] = words[i].BoundingBox.Top;
                    }
                }
                else
                {
                    // no overlap
                    if (words[i].BoundingBox.Bottom - currentProj[1] <= domFontHeight)
                    {
                        // if gap too small -> don't cut
                        // |____| |____|
                        currentProj[1] = words[i].BoundingBox.Top;
                    }
                    else
                    {
                        // if gap big enough -> cut!
                        // |____|   |   |____|
                        if (i != wordsCount - 1) // will always add the last one after
                        {
                            projectionProfile.Add(currentProj);
                            currentProj = new decimal[2] { words[i].BoundingBox.Bottom, words[i].BoundingBox.Top };
                        }
                    }
                }
                if (i == wordsCount - 1) projectionProfile.Add(currentProj);
            }

            if (projectionProfile.Count == 1)
            {
                if (level >= 1)
                {
                    return leaf;
                }
                else
                {
                    level++;
                }
            }

            var newLeafsEnums = projectionProfile.Select(p =>
                leaf.Words.Where(w => w.BoundingBox.Bottom >= p[0] && w.BoundingBox.Top <= p[1]));
            var newLeafs = newLeafsEnums.Where(e => e.Count() > 0).Select(e => new XYLeaf(e));
            var newNodes = newLeafs.Select(l => VerticalCut(l, minimumWidth,
                dominantFontWidthFunc, dominantFontHeightFunc, level)).ToList();

            var lost = leaf.Words.Except(newLeafsEnums.SelectMany(x => x)).Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
            if (lost.Count > 0)
            {
                newNodes.AddRange(lost.Select(w => new XYLeaf(w)));
            }
            return new XYNode(newNodes);
        }
    }

    /// <summary>
    /// A Node used in the <see cref="RecursiveXYCut"/> algorithm.
    /// </summary>
    public class XYNode
    {
        /// <summary>
        /// Returns true if this node is a leaf, false otherwise.
        /// </summary>
        public virtual bool IsLeaf => false;

        /// <summary>
        /// The rectangle completely containing the node.
        /// </summary>
        public PdfRectangle BoundingBox { get; set; }

        /// <summary>
        /// The children of the node.
        /// </summary>
        public XYNode[] Children { get; set; }

        /// <summary>
        /// Recursively counts the words included in this node.
        /// </summary>
        /// <returns></returns>
        public virtual int CountWords()
        {
            if (Children == null) return 0;
            int count = 0;
            RecursiveCount(Children, ref count);
            return count;
        }

        /// <summary>
        /// Recursively gets the leafs (last nodes) of this node.
        /// </summary>
        /// <returns></returns>
        public virtual List<XYLeaf> GetLeafs()
        {
            List<XYLeaf> leafs = new List<XYLeaf>();
            if (Children == null || Children.Count() == 0) return leafs;
            int level = 0;
            RecursiveGetLeafs(Children, ref leafs, level);
            return leafs;
        }

        /// <summary>
        /// Create a new <see cref="XYNode"/>.
        /// </summary>
        /// <param name="children"></param>
        public XYNode(params XYNode[] children)
            : this(children?.ToList())
        {

        }

        /// <summary>
        /// Create a new <see cref="XYNode"/>.
        /// </summary>
        /// <param name="children"></param>
        public XYNode(IEnumerable<XYNode> children)
        {
            if (children != null && children.Count() != 0)
            {
                Children = children.ToArray();
                decimal left = children.Min(b => b.BoundingBox.Left);
                decimal right = children.Max(b => b.BoundingBox.Right);
                decimal bottom = children.Min(b => b.BoundingBox.Bottom);
                decimal top = children.Max(b => b.BoundingBox.Top);
                BoundingBox = new PdfRectangle(left, bottom, right, top);
            }
        }

        private void RecursiveCount(IEnumerable<XYNode> children, ref int count)
        {
            if (children.Count() == 0) return;
            foreach (XYNode node in children.Where(x => x.IsLeaf))
            {
                count += node.CountWords();
            }

            foreach (XYNode node in children.Where(x => !x.IsLeaf))
            {
                RecursiveCount(node.Children, ref count);
            }
        }

        private void RecursiveGetLeafs(IEnumerable<XYNode> children, ref List<XYLeaf> leafs, int level)
        {
            if (children.Count() == 0) return;
            bool isVerticalCut = level % 2 == 0;

            foreach (XYLeaf node in children.Where(x => x.IsLeaf))
            {
                leafs.Add(node);
            }

            level++;

            IEnumerable<XYNode> notLeafs = children.Where(x => !x.IsLeaf);

            if (isVerticalCut)
            {
                notLeafs = notLeafs.OrderBy(x => x.BoundingBox.Left).ToList();
            }
            else
            {
                notLeafs = notLeafs.OrderByDescending(x => x.BoundingBox.Top).ToList();
            }

            foreach (XYNode node in notLeafs)
            {
                RecursiveGetLeafs(node.Children, ref leafs, level);
            }
        }

        public override string ToString()
        {
            return (IsLeaf ? "Leaf" : "Node");
        }
    }

    /// <summary>
    /// A Leaf node used in the <see cref="RecursiveXYCut"/> algorithm, i.e. a block.
    /// </summary>
    public class XYLeaf : XYNode
    {
        /// <summary>
        /// Returns true if this node is a leaf, false otherwise.
        /// </summary>
        public override bool IsLeaf => true;

        /// <summary>
        /// The words in the leaf.
        /// </summary>
        public Word[] Words { get; set; }

        /// <summary>
        /// The number of words in the leaf.
        /// </summary>
        public override int CountWords() => Words == null ? 0 : Words.Length;

        /// <summary>
        /// Returns null as a leaf doesn't have leafs.
        /// </summary>
        public override List<XYLeaf> GetLeafs()
        {
            return null;
        }

        /// <summary>
        /// Gets the lines of the leaf.
        /// </summary>
        public TextLine[] GetLines()
        {
            var groupedWords = Words.GroupBy(x => x.BoundingBox.Bottom).ToDictionary(x => x.Key, x => x.ToList());
            return groupedWords.OrderByDescending(x => x.Key).Select(ws => new TextLine(ws.Value)).ToArray();
        }

        /// <summary>
        /// Create a new <see cref="XYLeaf"/>.
        /// </summary>
        /// <param name="words">The words contained in the leaf.</param>
        public XYLeaf(params Word[] words) : this(words == null ? null : words.ToList())
        {

        }

        /// <summary>
        /// Create a new <see cref="XYLeaf"/>.
        /// </summary>
        /// <param name="words">The words contained in the leaf.</param>
        public XYLeaf(IEnumerable<Word> words) : base(null)
        {
            decimal left = words.Min(b => b.BoundingBox.Left);
            decimal right = words.Max(b => b.BoundingBox.Right);

            decimal bottom = words.Min(b => b.BoundingBox.Bottom);
            decimal top = words.Max(b => b.BoundingBox.Top);

            BoundingBox = new PdfRectangle(left, bottom, right, top);
            Words = words.ToArray();
        }
    }
}
