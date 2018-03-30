﻿namespace UglyToad.PdfPig.Fonts.TrueType.Parser
{
    using System;
    using System.Collections.Generic;
    using Tables;
    using Util.JetBrains.Annotations;

    internal class TrueTypeFontParser
    {
        public TrueTypeFont Parse(TrueTypeDataBytes data)
        {
            var version = (decimal)data.Read32Fixed();
            int numberOfTables = data.ReadUnsignedShort();
            int searchRange = data.ReadUnsignedShort();
            int entrySelector = data.ReadUnsignedShort();
            int rangeShift = data.ReadUnsignedShort();

            var tables = new Dictionary<string, TrueTypeHeaderTable>();

            for (var i = 0; i < numberOfTables; i++)
            {
                var table = ReadTable(data);

                if (table.HasValue)
                {
                    tables[table.Value.Tag] = table.Value;
                }
            }

            var result = ParseTables(version, tables, data);

            return result;
        }

        [CanBeNull]
        private static TrueTypeHeaderTable? ReadTable(TrueTypeDataBytes data)
        {
            var tag = data.ReadTag();
            var checksum = data.ReadUnsignedInt();
            var offset = data.ReadUnsignedInt();
            var length = data.ReadUnsignedInt();

            // skip tables with zero length (except glyf)
            if (length == 0 && !string.Equals(tag, TrueTypeHeaderTable.Glyf))
            {
                return null;
            }

            return new TrueTypeHeaderTable(tag, checksum, offset, length);
        }

        private static TrueTypeFont ParseTables(decimal version, IReadOnlyDictionary<string, TrueTypeHeaderTable> tables, TrueTypeDataBytes data)
        {
            var isPostScript = tables.ContainsKey(TrueTypeHeaderTable.Cff);

            var tableRegister = new TableRegister();

            if (!tables.TryGetValue(TrueTypeHeaderTable.Head, out var table))
            {
                throw new InvalidOperationException($"The {TrueTypeHeaderTable.Head} table is required.");
            }

            // head
            tableRegister.HeaderTable = HeaderTable.Load(data, table);

            if (!tables.TryGetValue(TrueTypeHeaderTable.Hhea, out var hHead))
            {
                throw new InvalidOperationException("The horizontal header table is required.");
            }

            // hhea
            tableRegister.HorizontalHeaderTable = HorizontalHeaderTable.Load(data, hHead);

            if (!tables.TryGetValue(TrueTypeHeaderTable.Maxp, out var maxHeaderTable))
            {
                throw new InvalidOperationException("The maximum profile table is required.");
            }

            // maxp
            tableRegister.MaximumProfileTable = BasicMaximumProfileTable.Load(data, maxHeaderTable);

            // post
            var postScriptTable = default(PostScriptTable);
            if (tables.TryGetValue(TrueTypeHeaderTable.Post, out var postscriptHeaderTable))
            {
                tableRegister.PostScriptTable = PostScriptTable.Load(data, table, tableRegister.MaximumProfileTable);
            }

            if (!isPostScript)
            {
                if (!tables.TryGetValue(TrueTypeHeaderTable.Loca, out var indexToLocationHeaderTable))
                {
                    throw new InvalidOperationException("The location to index table is required for non-PostScript fonts.");
                }

                // loca
                tableRegister.IndexToLocationTable =
                    IndexToLocationTable.Load(data, indexToLocationHeaderTable, tableRegister);

                if (!tables.TryGetValue(TrueTypeHeaderTable.Glyf, out var glyphHeaderTable))
                {
                    throw new InvalidOperationException("The glpyh table is required for non-PostScript fonts.");
                }

                // glyf
                tableRegister.GlyphDataTable = GlyphDataTable.Load(data, glyphHeaderTable, tableRegister);

                OptionallyParseTables(tables, data, tableRegister);
            }

            return new TrueTypeFont(version, tables, tableRegister.HeaderTable);
        }

        private static void OptionallyParseTables(IReadOnlyDictionary<string, TrueTypeHeaderTable> tables, TrueTypeDataBytes data, TableRegister tableRegister)
        {
            // cmap
            if (tables.TryGetValue(TrueTypeHeaderTable.Cmap, out var cmap))
            {
                tableRegister.CMapTable = CMapTable.Load(data, cmap, tableRegister);
            }

            // hmtx
            if (tables.TryGetValue(TrueTypeHeaderTable.Hmtx, out var hmtxHeaderTable))
            {
                tableRegister.HorizontalMetricsTable = HorizontalMetricsTable.Load(data, hmtxHeaderTable, tableRegister);
            }

            // name
            if (tables.TryGetValue(TrueTypeHeaderTable.Name, out var nameHeaderTable))
            {
                // TODO: Not important
            }

            // os2

            // kern
        }
    }
}

