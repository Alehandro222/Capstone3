using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackerApi.Migrations.Postgres
{
    // Supabase exposes the public schema to its anon/authenticated roles through its Data API.
    // Row-level security with no policies shuts those roles out of every table. The API is not
    // affected: it signs in as the tables' owner (postgres), which skips RLS. Enabling RLS on a
    // table that already has it is harmless, so this also runs cleanly on the live database,
    // where it was first turned on by hand (15 Nov 2026).
    // Tables added by later migrations need their own ENABLE ROW LEVEL SECURITY.
    /// <inheritdoc />
    public partial class EnableRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Every("ENABLE"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Every("DISABLE"));
        }

        // Every table in the public schema, including __EFMigrationsHistory.
        private static string Every(string action) => $"""
            DO $$
            DECLARE t record;
            BEGIN
              FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                EXECUTE format('ALTER TABLE public.%I {action} ROW LEVEL SECURITY', t.tablename);
              END LOOP;
            END $$;
            """;
    }
}
