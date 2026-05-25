using System;
using Raven.Server.Integrations.PostgreSQL;
using Raven.Server.Integrations.PostgreSQL.Classification;
using Raven.Server.Integrations.PostgreSQL.Npgsql;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Server.Integrations.PostgreSQL
{
    // Test fixtures: the exact query strings Npgsql drivers send on startup. Kept here (not in
    // NpgsqlConfig) because production code classifies queries by AST shape — it never compares
    // against these verbatim strings. They exist only to drive the classifier tests below.
    internal static class NpgsqlTestQueries
    {
        // Npgsql 5.0.0+ — "Load enum fields" probe (line-comment variant).
        public const string Npgsql5EnumTypesQuery =
            "-- Load enum fields\nSELECT pg_type.oid, enumlabel\nFROM pg_enum\nJOIN pg_type ON pg_type.oid=enumtypid\nORDER BY oid, enumsortorder";

        // Npgsql 4.0.0–4.1.1 — same probe with a C-style block comment.
        public const string EnumTypesQuery =
            "/*** Load enum fields ***/\nSELECT pg_type.oid, enumlabel\nFROM pg_enum\nJOIN pg_type ON pg_type.oid=enumtypid\nORDER BY oid, enumsortorder";

        // Npgsql 5.0.0+ — "Load field definitions for composite types" probe (line-comment variant).
        public const string Npgsql5CompositeTypesQuery =
            "-- Load field definitions for (free-standing) composite types\nSELECT typ.oid, att.attname, att.atttypid\nFROM pg_type AS typ\nJOIN pg_namespace AS ns ON (ns.oid = typ.typnamespace)\nJOIN pg_class AS cls ON (cls.oid = typ.typrelid)\nJOIN pg_attribute AS att ON (att.attrelid = typ.typrelid)\nWHERE\n  (typ.typtype = 'c' AND cls.relkind='c') AND\n  attnum > 0 AND     -- Don't load system attributes\n  NOT attisdropped\nORDER BY typ.oid, att.attnum";

        // Npgsql 4.0.4–4.1.1 — same probe with block comments.
        public const string CompositeTypesQuery =
            "/*** Load field definitions for (free-standing) composite types ***/\nSELECT typ.oid, att.attname, att.atttypid\nFROM pg_type AS typ\nJOIN pg_namespace AS ns ON (ns.oid = typ.typnamespace)\nJOIN pg_class AS cls ON (cls.oid = typ.typrelid)\nJOIN pg_attribute AS att ON (att.attrelid = typ.typrelid)\nWHERE\n  (typ.typtype = 'c' AND cls.relkind='c') AND\n  attnum > 0 AND     /* Don't load system attributes */\n  NOT attisdropped\nORDER BY typ.oid, att.attnum";

        // Npgsql 4.0.0–4.0.3 — composite probe sorted by typname instead of oid.
        public const string Npgsql4_0_0CompositeTypesQuery =
            "/*** Load field definitions for (free-standing) composite types ***/\nSELECT typ.oid, att.attname, att.atttypid\nFROM pg_type AS typ\nJOIN pg_namespace AS ns ON (ns.oid = typ.typnamespace)\nJOIN pg_class AS cls ON (cls.oid = typ.typrelid)\nJOIN pg_attribute AS att ON (att.attrelid = typ.typrelid)\nWHERE\n  (typ.typtype = 'c' AND cls.relkind='c') AND\n  attnum > 0 AND     /* Don't load system attributes */\n  NOT attisdropped\nORDER BY typ.typname, att.attnum";

        // Npgsql 5.0.0+ — type-catalog load (modern nested shape, 13 columns).
        public const string Npgsql5TypesQuery =
            "SELECT ns.nspname, typ_and_elem_type.*,\n   CASE\n       WHEN typtype IN ('b', 'e', 'p') THEN 0           -- First base types, enums, pseudo-types\n       WHEN typtype = 'r' THEN 1                        -- Ranges after\n       WHEN typtype = 'c' THEN 2                        -- Composites after\n       WHEN typtype = 'd' AND elemtyptype <> 'a' THEN 3 -- Domains over non-arrays after\n       WHEN typtype = 'a' THEN 4                        -- Arrays before\n       WHEN typtype = 'd' AND elemtyptype = 'a' THEN 5  -- Domains over arrays last\n    END AS ord\nFROM (\n    -- Arrays have typtype=b - this subquery identifies them by their typreceive and converts their typtype to a\n    -- We first do this for the type (innerest-most subquery), and then for its element type\n    -- This also returns the array element, range subtype and domain base type as elemtypoid\n    SELECT\n        typ.oid, typ.typnamespace, typ.typname, typ.typtype, typ.typrelid, typ.typnotnull, typ.relkind,\n        elemtyp.oid AS elemtypoid, elemtyp.typname AS elemtypname, elemcls.relkind AS elemrelkind,\n        CASE WHEN elemproc.proname='array_recv' THEN 'a' ELSE elemtyp.typtype END AS elemtyptype\n    FROM (\n        SELECT typ.oid, typnamespace, typname, typrelid, typnotnull, relkind, typelem AS elemoid,\n            CASE WHEN proc.proname='array_recv' THEN 'a' ELSE typ.typtype END AS typtype,\n            CASE\n                WHEN proc.proname='array_recv' THEN typ.typelem\n                WHEN typ.typtype='r' THEN rngsubtype\n                WHEN typ.typtype='d' THEN typ.typbasetype\n            END AS elemtypoid\n        FROM pg_type AS typ\n        LEFT JOIN pg_class AS cls ON (cls.oid = typ.typrelid)\n        LEFT JOIN pg_proc AS proc ON proc.oid = typ.typreceive\n        LEFT JOIN pg_range ON (pg_range.rngtypid = typ.oid)\n    ) AS typ\n    LEFT JOIN pg_type AS elemtyp ON elemtyp.oid = elemtypoid\n    LEFT JOIN pg_class AS elemcls ON (elemcls.oid = elemtyp.typrelid)\n    LEFT JOIN pg_proc AS elemproc ON elemproc.oid = elemtyp.typreceive\n) AS typ_and_elem_type\nJOIN pg_namespace AS ns ON (ns.oid = typnamespace)\nWHERE\n    typtype IN ('b', 'r', 'e', 'd') OR -- Base, range, enum, domain\n    (typtype = 'c' AND relkind='c') OR -- User-defined free-standing composites (not table composites) by default\n    (typtype = 'p' AND typname IN ('record', 'void')) OR -- Some special supported pseudo-types\n    (typtype = 'a' AND (  -- Array of...\n        elemtyptype IN ('b', 'r', 'e', 'd') OR -- Array of base, range, enum, domain\n        (elemtyptype = 'p' AND elemtypname IN ('record', 'void')) OR -- Arrays of special supported pseudo-types\n        (elemtyptype = 'c' AND elemrelkind='c') -- Array of user-defined free-standing composites (not table composites) by default\n    ))\nORDER BY ord";

        // Npgsql 4.1.3–4.1.9 — same as Npgsql5TypesQuery but with a leading newline (AST-identical).
        public const string Npgsql4TypesQuery = "\n" + Npgsql5TypesQuery;

        // Npgsql 4.1.0–4.1.2 — mid flat shape (7 columns, includes typelem).
        public const string Npgsql4_1_2TypesQuery =
            "\n/*** Load all supported types ***/\nSELECT ns.nspname, a.typname, a.oid, a.typbasetype,\nCASE WHEN pg_proc.proname='array_recv' THEN 'a' ELSE a.typtype END AS typtype,\nCASE\n  WHEN pg_proc.proname='array_recv' THEN a.typelem\n  WHEN a.typtype='r' THEN rngsubtype\n  ELSE 0\nEND AS typelem,\nCASE\n  WHEN a.typtype='d' AND a.typcategory='A' THEN 4 /* Domains over arrays last */\n  WHEN pg_proc.proname IN ('array_recv','oidvectorrecv') THEN 3    /* Arrays before */\n  WHEN a.typtype='r' THEN 2                                        /* Ranges before */\n  WHEN a.typtype='d' THEN 1                                        /* Domains before */\n  ELSE 0                                                           /* Base types first */\nEND AS ord\nFROM pg_type AS a\nJOIN pg_namespace AS ns ON (ns.oid = a.typnamespace)\nJOIN pg_proc ON pg_proc.oid = a.typreceive\nLEFT OUTER JOIN pg_class AS cls ON (cls.oid = a.typrelid)\nLEFT OUTER JOIN pg_type AS b ON (b.oid = a.typelem)\nLEFT OUTER JOIN pg_class AS elemcls ON (elemcls.oid = b.typrelid)\nLEFT OUTER JOIN pg_range ON (pg_range.rngtypid = a.oid) \nWHERE\n  a.typtype IN ('b', 'r', 'e', 'd') OR         /* Base, range, enum, domain */\n  (a.typtype = 'c' AND cls.relkind='c') OR /* User-defined free-standing composites (not table composites) by default */\n  (pg_proc.proname='array_recv' AND (\n    b.typtype IN ('b', 'r', 'e', 'd') OR       /* Array of base, range, enum, domain */\n    (b.typtype = 'p' AND b.typname IN ('record', 'void')) OR /* Arrays of special supported pseudo-types */\n    (b.typtype = 'c' AND elemcls.relkind='c')  /* Array of user-defined free-standing composites (not table composites) */\n  )) OR\n  (a.typtype = 'p' AND a.typname IN ('record', 'void'))  /* Some special supported pseudo-types */\nORDER BY ord";

        // Npgsql 4.0.3 — old flat shape with pseudo-type arrays (leading comment, no blank line).
        public const string Npgsql4_0_3TypesQuery =
            "/*** Load all supported types ***/\nSELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype,\nCASE WHEN pg_proc.proname='array_recv' THEN 'a' ELSE a.typtype END AS type,\nCASE\n  WHEN pg_proc.proname='array_recv' THEN a.typelem\n  WHEN a.typtype='r' THEN rngsubtype\n  ELSE 0\nEND AS elemoid,\nCASE\n  WHEN pg_proc.proname IN ('array_recv','oidvectorrecv') THEN 3    /* Arrays last */\n  WHEN a.typtype='r' THEN 2                                        /* Ranges before */\n  WHEN a.typtype='d' THEN 1                                        /* Domains before */\n  ELSE 0                                                           /* Base types first */\nEND AS ord\nFROM pg_type AS a\nJOIN pg_namespace AS ns ON (ns.oid = a.typnamespace)\nJOIN pg_proc ON pg_proc.oid = a.typreceive\nLEFT OUTER JOIN pg_class AS cls ON (cls.oid = a.typrelid)\nLEFT OUTER JOIN pg_type AS b ON (b.oid = a.typelem)\nLEFT OUTER JOIN pg_class AS elemcls ON (elemcls.oid = b.typrelid)\nLEFT OUTER JOIN pg_range ON (pg_range.rngtypid = a.oid) \nWHERE\n  a.typtype IN ('b', 'r', 'e', 'd') OR         /* Base, range, enum, domain */\n  (a.typtype = 'c' AND cls.relkind='c') OR /* User-defined free-standing composites (not table composites) by default */\n  (pg_proc.proname='array_recv' AND (\n    b.typtype IN ('b', 'r', 'e', 'd') OR       /* Array of base, range, enum, domain */\n    (b.typtype = 'p' AND b.typname IN ('record', 'void')) OR /* Arrays of special supported pseudo-types */\n    (b.typtype = 'c' AND elemcls.relkind='c')  /* Array of user-defined free-standing composites (not table composites) */\n  )) OR\n  (a.typtype = 'p' AND a.typname IN ('record', 'void'))  /* Some special supported pseudo-types */\nORDER BY ord";

        // Npgsql 4.0.1–4.0.12 (except 4.0.3) — same content as Npgsql4_0_3TypesQuery but with a leading newline.
        public const string TypesQuery = "\n" + Npgsql4_0_3TypesQuery;

        // Npgsql 4.0.0 — old flat shape without pseudo-type arrays (one OR-branch fewer than TypesQuery).
        public const string Npgsql4_0_0TypesQuery =
            "\n/*** Load all supported types ***/\nSELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype,\nCASE WHEN pg_proc.proname='array_recv' THEN 'a' ELSE a.typtype END AS type,\nCASE\n  WHEN pg_proc.proname='array_recv' THEN a.typelem\n  WHEN a.typtype='r' THEN rngsubtype\n  ELSE 0\nEND AS elemoid,\nCASE\n  WHEN pg_proc.proname IN ('array_recv','oidvectorrecv') THEN 3    /* Arrays last */\n  WHEN a.typtype='r' THEN 2                                        /* Ranges before */\n  WHEN a.typtype='d' THEN 1                                        /* Domains before */\n  ELSE 0                                                           /* Base types first */\nEND AS ord\nFROM pg_type AS a\nJOIN pg_namespace AS ns ON (ns.oid = a.typnamespace)\nJOIN pg_proc ON pg_proc.oid = a.typreceive\nLEFT OUTER JOIN pg_class AS cls ON (cls.oid = a.typrelid)\nLEFT OUTER JOIN pg_type AS b ON (b.oid = a.typelem)\nLEFT OUTER JOIN pg_class AS elemcls ON (elemcls.oid = b.typrelid)\nLEFT OUTER JOIN pg_range ON (pg_range.rngtypid = a.oid) \nWHERE\n  a.typtype IN ('b', 'r', 'e', 'd') OR         /* Base, range, enum, domain */\n  (a.typtype = 'c' AND cls.relkind='c') OR /* User-defined free-standing composites (not table composites) by default */\n  (pg_proc.proname='array_recv' AND (\n    b.typtype IN ('b', 'r', 'e', 'd') OR       /* Array of base, range, enum domain */\n    (b.typtype = 'c' AND elemcls.relkind='c')  /* Array of user-defined free-standing composites (not table composites) */\n  )) OR\n  (a.typtype = 'p' AND a.typname IN ('record', 'void'))  /* Some special supported pseudo-types */\nORDER BY ord";

        // Npgsql 3.2.3–3.2.7 — legacy shape (no pg_class join, no LEFT OUTER JOIN pg_class).
        public const string Npgsql3TypesQuery =
            "SELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype,\nCASE WHEN pg_proc.proname='array_recv' THEN 'a' ELSE a.typtype END AS type,\nCASE\n  WHEN pg_proc.proname='array_recv' THEN a.typelem\n  WHEN a.typtype='r' THEN rngsubtype\n  ELSE 0\nEND AS elemoid,\nCASE\n  WHEN pg_proc.proname IN ('array_recv','oidvectorrecv') THEN 3    /* Arrays last */\n  WHEN a.typtype='r' THEN 2                                        /* Ranges before */\n  WHEN a.typtype='d' THEN 1                                        /* Domains before */\n  ELSE 0                                                           /* Base types first */\nEND AS ord\nFROM pg_type AS a\nJOIN pg_namespace AS ns ON (ns.oid = a.typnamespace)\nJOIN pg_proc ON pg_proc.oid = a.typreceive\nLEFT OUTER JOIN pg_type AS b ON (b.oid = a.typelem)\nLEFT OUTER JOIN pg_range ON (pg_range.rngtypid = a.oid) \nWHERE\n  (\n    a.typtype IN ('b', 'r', 'e', 'd') AND\n    (b.typtype IS NULL OR b.typtype IN ('b', 'r', 'e', 'd'))  /* Either non-array or array of supported element type */\n  ) OR\n  (a.typname IN ('record', 'void') AND a.typtype = 'p')\nORDER BY ord";
    }

    // Tests for NpgsqlQueryClassifier: TryMatchSimpleQuery, TryMatchMetadataQuery, TryMatchTypesQuery.
    public sealed class NpgsqlQueryClassifierTests(ITestOutputHelper output) : NoDisposalNeeded(output)
    {
        // ── Simple queries — version() and current_setting(...) ──────────────────────────────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Version_canonical_lowercase_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("select version()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Version_uppercase_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("SELECT VERSION()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Version_with_extra_whitespace_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("  select   version(  )  ", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Version_with_leading_newline_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("\nselect version()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Version_with_crlf_line_endings_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("select\r\nversion()", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Version_with_trailing_semicolon_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("select version();", out var table));
            Assert.Same(NpgsqlConfig.VersionResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CurrentSetting_canonical_lowercase_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("select current_setting('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CurrentSetting_uppercase_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("SELECT CURRENT_SETTING('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CurrentSetting_with_extra_whitespace_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("  select  current_setting( 'max_index_keys' )  ", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CurrentSetting_with_crlf_line_endings_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("select\r\ncurrent_setting('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CurrentSetting_with_trailing_semicolon_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery("select current_setting('max_index_keys');", out var table));
            Assert.Same(NpgsqlConfig.CurrentSettingResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void VersionAndCurrentSetting_combined_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery(
                "select version();select current_setting('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.VersionCurrentSettingResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void VersionAndCurrentSetting_combined_with_whitespace_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchSimpleQuery(
                "SELECT VERSION() ; SELECT CURRENT_SETTING('max_index_keys')", out var table));
            Assert.Same(NpgsqlConfig.VersionCurrentSettingResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Simple_different_function_name_should_not_match()
        {
            Assert.False(NpgsqlQueryClassifier.TryMatchSimpleQuery("select pg_version()", out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CurrentSetting_with_wrong_key_should_not_match()
        {
            Assert.False(NpgsqlQueryClassifier.TryMatchSimpleQuery("select current_setting('some_other_key')", out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Version_with_from_clause_should_not_match()
        {
            Assert.False(NpgsqlQueryClassifier.TryMatchSimpleQuery("select version() from pg_catalog.pg_settings", out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Simple_empty_query_should_not_match()
        {
            Assert.False(NpgsqlQueryClassifier.TryMatchSimpleQuery("", out _));
            Assert.False(NpgsqlQueryClassifier.TryMatchSimpleQuery("   ", out _));
        }

        // ── Metadata queries — enum types and composite types ─────────────────────────────────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void EnumTypes_block_comment_variant_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchMetadataQuery(NpgsqlTestQueries.EnumTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.EnumTypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void EnumTypes_line_comment_variant_should_match_same_response()
        {
            // Comments stripped by parser → AST identical to block-comment variant.
            Assert.True(NpgsqlQueryClassifier.TryMatchMetadataQuery(NpgsqlTestQueries.Npgsql5EnumTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.EnumTypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void EnumTypes_whitespace_variant_should_match()
        {
            const string query =
                "SELECT  pg_type.oid ,  enumlabel  " +
                "FROM  pg_enum  " +
                "JOIN  pg_type  ON  pg_type.oid = enumtypid  " +
                "ORDER BY  oid ,  enumsortorder";
            Assert.True(NpgsqlQueryClassifier.TryMatchMetadataQuery(query, out var table));
            Assert.Same(NpgsqlConfig.EnumTypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void EnumTypes_uppercase_keywords_should_match()
        {
            const string query =
                "SELECT PG_TYPE.OID, ENUMLABEL FROM PG_ENUM JOIN PG_TYPE ON PG_TYPE.OID=ENUMTYPID ORDER BY OID, ENUMSORTORDER";
            Assert.True(NpgsqlQueryClassifier.TryMatchMetadataQuery(query, out var table));
            Assert.Same(NpgsqlConfig.EnumTypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CompositeTypes_block_comment_variant_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchMetadataQuery(NpgsqlTestQueries.CompositeTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.CompositeTypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CompositeTypes_line_comment_variant_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchMetadataQuery(NpgsqlTestQueries.Npgsql5CompositeTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.CompositeTypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CompositeTypes_old_orderby_variant_should_match()
        {
            // 4.0.0–4.0.3: ORDER BY typ.typname instead of typ.oid — AST matcher ignores ORDER BY.
            Assert.True(NpgsqlQueryClassifier.TryMatchMetadataQuery(NpgsqlTestQueries.Npgsql4_0_0CompositeTypesQuery, out var table));
            Assert.Same(NpgsqlConfig.CompositeTypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Metadata_empty_query_should_not_match()
        {
            Assert.False(NpgsqlQueryClassifier.TryMatchMetadataQuery("", out _));
            Assert.False(NpgsqlQueryClassifier.TryMatchMetadataQuery("   ", out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void EnumTypes_with_wrong_columns_should_not_match()
        {
            const string query =
                "SELECT pg_type.oid, enumlabel, enumsortorder FROM pg_enum JOIN pg_type ON pg_type.oid=enumtypid ORDER BY oid";
            Assert.False(NpgsqlQueryClassifier.TryMatchMetadataQuery(query, out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CompositeTypes_wrong_columns_should_not_match()
        {
            const string query =
                "SELECT typ.oid, att.attname FROM pg_type AS typ JOIN pg_class AS cls ON cls.oid = typ.typrelid JOIN pg_attribute AS att ON att.attrelid = typ.typrelid";
            Assert.False(NpgsqlQueryClassifier.TryMatchMetadataQuery(query, out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CompositeTypes_missing_pg_attribute_should_not_match()
        {
            const string query =
                "SELECT typ.oid, att.attname, att.atttypid FROM pg_type AS typ JOIN pg_class AS cls ON cls.oid = typ.typrelid WHERE typ.typtype = 'c'";
            Assert.False(NpgsqlQueryClassifier.TryMatchMetadataQuery(query, out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Unrelated_pg_catalog_query_should_not_match()
        {
            Assert.False(NpgsqlQueryClassifier.TryMatchMetadataQuery(
                "SELECT oid, typname FROM pg_type WHERE typtype = 'b' ORDER BY oid", out _));
        }

        // ── Type-loading — Family A (modern nested, Npgsql 4.1.3–5.x+) ──────────────────────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyA_Npgsql5TypesQuery_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchTypesQuery(NpgsqlTestQueries.Npgsql5TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql5TypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyA_Npgsql4TypesQuery_should_match_same_response()
        {
            // Differs from Npgsql5TypesQuery only by a leading \n → identical AST → same response.
            Assert.True(NpgsqlQueryClassifier.TryMatchTypesQuery(NpgsqlTestQueries.Npgsql4TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql5TypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyA_leading_newline_variant_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchTypesQuery("\n" + NpgsqlTestQueries.Npgsql5TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql5TypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyA_subquery_in_from_but_wrong_shape_should_not_match()
        {
            // Has a RangeSubselect in FROM but only 2 targets and no .* wildcard.
            const string query =
                "SELECT typname, oid FROM (SELECT typname, oid FROM pg_type WHERE typtype = 'b') AS base_types";
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery(query, out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyA_subquery_in_from_three_targets_but_no_wildcard_should_not_match()
        {
            const string query =
                "SELECT a, b, c FROM (SELECT a, b, c FROM pg_type) AS sub JOIN pg_namespace AS ns ON true";
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery(query, out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyA_empty_query_should_not_match()
        {
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery("", out _));
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery("   ", out _));
        }

        // ── Type-loading — Family B (mid flat, Npgsql 4.1.0–4.1.2) ──────────────────────────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyB_Npgsql4_1_2TypesQuery_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchTypesQuery(NpgsqlTestQueries.Npgsql4_1_2TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql4_1_2TypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyB_seven_column_query_without_typelem_should_not_match()
        {
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typbasetype, a.typtype, a.typalign, a.ord " +
                "FROM pg_type AS a JOIN pg_namespace AS ns ON ns.oid = a.typnamespace";
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery(query, out _));
        }

        // ── Type-loading — Family E (legacy Npgsql 3, 3.2.3–3.2.7) ─────────────────────────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyE_Npgsql3TypesQuery_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchTypesQuery(NpgsqlTestQueries.Npgsql3TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql3TypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyE_eight_column_query_without_pg_proc_should_not_match()
        {
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype, a.typtype, a.typelem, a.ord " +
                "FROM pg_type AS a JOIN pg_namespace AS ns ON ns.oid = a.typnamespace";
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery(query, out _));
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyE_eight_column_pg_type_pg_proc_no_pg_class_wrong_columns_should_not_match()
        {
            // Has 8 cols + pg_type + pg_proc + no pg_class but wrong column names (typtype/typelem vs type/elemoid).
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype, a.typtype, a.typelem, a.ord " +
                "FROM pg_type AS a " +
                "JOIN pg_namespace AS ns ON ns.oid = a.typnamespace " +
                "JOIN pg_proc AS p ON p.oid = a.typreceive";
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery(query, out _));
        }

        // ── Type-loading — Family C (old flat + pseudo-type arrays, Npgsql 4.0.1–4.0.12) ─────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyC_TypesQuery_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchTypesQuery(NpgsqlTestQueries.TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.TypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyC_Npgsql4_0_3TypesQuery_should_match_same_response()
        {
            // Differs from TypesQuery only by leading comment/whitespace → identical AST → same response.
            Assert.True(NpgsqlQueryClassifier.TryMatchTypesQuery(NpgsqlTestQueries.Npgsql4_0_3TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.TypesResponse, table);
        }

        // ── Type-loading — Family D (old flat without pseudo-type arrays, Npgsql 4.0.0) ──────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyD_Npgsql4_0_0TypesQuery_should_match()
        {
            Assert.True(NpgsqlQueryClassifier.TryMatchTypesQuery(NpgsqlTestQueries.Npgsql4_0_0TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.Npgsql4_0_0TypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyD_TypesQuery_still_maps_to_C_not_D()
        {
            // Family C claims TypesQuery (pseudo-type branch present); Family D must not steal it.
            Assert.True(NpgsqlQueryClassifier.TryMatchTypesQuery(NpgsqlTestQueries.TypesQuery, out var table));
            Assert.Same(NpgsqlConfig.TypesResponse, table);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyD_query_without_array_recv_block_should_not_match()
        {
            // Right shape but WHERE has no array_recv AND-block — positive structural anchor must fail.
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typrelid, a.typbasetype, " +
                "a.typtype AS type, a.typelem AS elemoid, 0 AS ord " +
                "FROM pg_type AS a " +
                "JOIN pg_namespace AS ns ON ns.oid = a.typnamespace " +
                "JOIN pg_proc AS p ON p.oid = a.typreceive " +
                "LEFT JOIN pg_class AS cls ON cls.oid = a.typrelid " +
                "WHERE a.typtype IN ('b', 'r', 'e', 'd')";
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery(query, out _));
        }

        // ── Dispatch-level (through HardcodedQuery.TryParse; session=null is safe here) ──────────

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_Version_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse("select version()", Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_CurrentSetting_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(
                "select current_setting('max_index_keys')", Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_VersionAndCurrentSetting_combined_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(
                "select version();select current_setting('max_index_keys')", Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_Version_with_whitespace_variation_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse("  SELECT  VERSION(  )  ", Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_EnumTypes_block_comment_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.EnumTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_EnumTypes_line_comment_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.Npgsql5EnumTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_CompositeTypes_block_comment_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.CompositeTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_CompositeTypes_line_comment_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.Npgsql5CompositeTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_CompositeTypes_old_orderby_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.Npgsql4_0_0CompositeTypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_Npgsql5TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.Npgsql5TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_Npgsql4TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.Npgsql4TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_Npgsql4_1_2TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.Npgsql4_1_2TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_Npgsql3TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.Npgsql3TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_Npgsql4_0_3TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.Npgsql4_0_3TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void HardcodedQuery_Npgsql4_0_0TypesQuery_is_claimed_at_dispatch_level()
        {
            Assert.True(HardcodedQuery.TryParse(NpgsqlTestQueries.Npgsql4_0_0TypesQuery, Array.Empty<int>(), session: null, out var query));
            Assert.NotNull(query);
        }

        // ── Intent-based tolerance: reordered projections in metadata queries ────────────

        // Set-based projection-name matching: {oid, enumlabel} in any order should match EnumTypes.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void EnumTypes_reordered_projection_should_match()
        {
            const string query =
                "SELECT enumlabel, pg_type.oid FROM pg_enum JOIN pg_type ON pg_type.oid = enumtypid";

            Assert.True(NpgsqlQueryClassifier.TryMatchMetadataQuery(query, out var table));
            Assert.Same(NpgsqlConfig.EnumTypesResponse, table);
        }

        // {oid, attname, atttypid} in any order should match CompositeTypes.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void CompositeTypes_reordered_projection_should_match()
        {
            const string query =
                "SELECT att.atttypid, att.attname, typ.oid FROM pg_type AS typ " +
                "JOIN pg_class AS cls ON cls.oid = typ.typrelid " +
                "JOIN pg_attribute AS att ON att.attrelid = typ.typrelid WHERE typ.typtype = 'c'";

            Assert.True(NpgsqlQueryClassifier.TryMatchMetadataQuery(query, out var table));
            Assert.Same(NpgsqlConfig.CompositeTypesResponse, table);
        }

        // ── Cross-source guard: Npgsql recognizer must not claim PowerBI queries ──────────

        // A representative PowerBI metadata query (CharacterSets) uses information_schema
        // tables — completely different from any Npgsql intent.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void Npgsql_recognizer_does_not_claim_PowerBI_character_sets_query()
        {
            const string query =
                "SELECT character_set_name FROM information_schema.character_sets";

            Assert.False(NpgsqlQueryClassifier.TryMatchSimpleQuery(query, out _));
            Assert.False(NpgsqlQueryClassifier.TryMatchMetadataQuery(query, out _));
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery(query, out _));
        }

        // ── Multi-statement: only the exact version+current_setting pair is accepted ──────

        // Two statements where the second is NOT the expected current_setting probe.
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void TwoStatements_not_version_and_current_setting_should_not_match()
        {
            // Both are version() calls — not the recognized pair.
            Assert.False(NpgsqlQueryClassifier.TryMatchSimpleQuery(
                "select version(); select version()", out _));
        }

        // Three statements are never recognized (Npgsql only sends 1 or 2).
        [RavenFact(RavenTestCategory.PostgreSql)]
        public void ThreeStatements_should_not_match_any_intent()
        {
            const string query =
                "select version(); select current_setting('max_index_keys'); select version()";

            Assert.False(NpgsqlQueryClassifier.TryMatchSimpleQuery(query, out _));
            Assert.False(NpgsqlQueryClassifier.TryMatchMetadataQuery(query, out _));
            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery(query, out _));
        }

        // ── MidFlat tightening: 7 columns + typelem but wrong source tables must not match ─

        [RavenFact(RavenTestCategory.PostgreSql)]
        public void FamilyB_seven_column_with_typelem_but_no_pg_type_should_not_match()
        {
            // Has 7 columns including typelem but uses a non-pg_catalog table.
            const string query =
                "SELECT ns.nspname, a.typname, a.oid, a.typbasetype, a.typtype, a.typelem, a.ord " +
                "FROM my_schema.my_types AS a JOIN my_schema.my_namespaces AS ns ON ns.oid = a.typnamespace";

            Assert.False(NpgsqlQueryClassifier.TryMatchTypesQuery(query, out _));
        }
    }
}
