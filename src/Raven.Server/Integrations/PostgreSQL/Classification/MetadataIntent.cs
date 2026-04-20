using Raven.Server.Integrations.PostgreSQL.Messages;
using Raven.Server.Integrations.PostgreSQL.Npgsql;
using Raven.Server.Integrations.PostgreSQL.PowerBI;

namespace Raven.Server.Integrations.PostgreSQL.Classification
{
    /// <summary>
    /// Semantic categories of metadata/initialization queries we classify coming from
    /// PowerBI and Npgsql. Each value describes *what the client is asking for*, not the
    /// specific SQL family or historical query string that produced it.
    ///
    /// Classification is feature-based (source relations, projected column-name set,
    /// structural traits) via <see cref="PowerBIQueryClassifier"/> and
    /// <see cref="NpgsqlQueryClassifier"/>. Each classified intent maps to one of the
    /// canonical <see cref="PgTable"/> responses in <see cref="PowerBIConfig"/> or
    /// <see cref="NpgsqlConfig"/> via <see cref="TryResolveToResponse"/>.
    /// </summary>
    internal enum MetadataIntent
    {
        // ── PowerBI metadata intents ─────────────────────────────────────────────────────
        CharacterSets,
        PrimaryKeyConstraints,
        ForeignKeyRelationshipsFkCentric,
        ForeignKeyRelationshipsPkCentric,
        ForeignKeyRelationshipsReferential,

        // ── Npgsql protocol probes ───────────────────────────────────────────────────────
        ServerVersion,
        MaxIndexKeys,
        ServerVersionAndMaxIndexKeys,

        // ── Npgsql pg_catalog metadata ───────────────────────────────────────────────────
        EnumTypeLabels,
        CompositeTypeFields,

        // ── Npgsql type-catalog loading (one per response shape) ─────────────────────────
        TypeCatalogModernNested,
        TypeCatalogMidFlat,
        TypeCatalogLegacyV3,
        TypeCatalogOldFlatWithPseudoArrays,
        TypeCatalogOldFlatWithoutPseudoArrays,
    }

    internal static class MetadataIntentExtensions
    {
        /// <summary>
        /// Resolves a classified <see cref="MetadataIntent"/> to the canonical
        /// <see cref="PgTable"/> response. This is the only place that names the
        /// intent → response mapping; classifiers stay free of specific response references.
        ///
        /// Notable: <see cref="MetadataIntent.ForeignKeyRelationshipsReferential"/> always returns
        /// <see cref="PowerBIConfig.TableSchemaResponse"/> regardless of whether the incoming query
        /// projected FK-centric or PK-centric columns. The response is always empty (RavenDB has no
        /// SQL foreign keys) and PowerBI accepts either column shape for 0 rows.
        /// </summary>
        public static bool TryResolveToResponse(this MetadataIntent intent, out PgTable response)
        {
            switch (intent)
            {
                case MetadataIntent.CharacterSets:
                    response = PowerBIConfig.CharacterSetsResponse;
                    return true;

                case MetadataIntent.PrimaryKeyConstraints:
                    response = PowerBIConfig.ConstraintsResponse;
                    return true;

                case MetadataIntent.ForeignKeyRelationshipsFkCentric:
                    response = PowerBIConfig.TableSchemaResponse;
                    return true;

                case MetadataIntent.ForeignKeyRelationshipsPkCentric:
                    response = PowerBIConfig.TableSchemaSecondaryResponse;
                    return true;

                case MetadataIntent.ForeignKeyRelationshipsReferential:
                    // Historical: always FK-centric response (0 rows — RavenDB has no SQL FKs).
                    response = PowerBIConfig.TableSchemaResponse;
                    return true;

                case MetadataIntent.ServerVersion:
                    response = NpgsqlConfig.VersionResponse;
                    return true;

                case MetadataIntent.MaxIndexKeys:
                    response = NpgsqlConfig.CurrentSettingResponse;
                    return true;

                case MetadataIntent.ServerVersionAndMaxIndexKeys:
                    response = NpgsqlConfig.VersionCurrentSettingResponse;
                    return true;

                case MetadataIntent.EnumTypeLabels:
                    response = NpgsqlConfig.EnumTypesResponse;
                    return true;

                case MetadataIntent.CompositeTypeFields:
                    response = NpgsqlConfig.CompositeTypesResponse;
                    return true;

                case MetadataIntent.TypeCatalogModernNested:
                    // Npgsql5TypesResponse and Npgsql4TypesResponse are data-identical.
                    response = NpgsqlConfig.Npgsql5TypesResponse;
                    return true;

                case MetadataIntent.TypeCatalogMidFlat:
                    response = NpgsqlConfig.Npgsql4_1_2TypesResponse;
                    return true;

                case MetadataIntent.TypeCatalogLegacyV3:
                    response = NpgsqlConfig.Npgsql3TypesResponse;
                    return true;

                case MetadataIntent.TypeCatalogOldFlatWithPseudoArrays:
                    // TypesResponse and Npgsql4_0_3TypesResponse are data-identical.
                    response = NpgsqlConfig.TypesResponse;
                    return true;

                case MetadataIntent.TypeCatalogOldFlatWithoutPseudoArrays:
                    response = NpgsqlConfig.Npgsql4_0_0TypesResponse;
                    return true;

                default:
                    response = null;
                    return false;
            }
        }
    }
}
