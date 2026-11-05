using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AjoCircle.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database-level backstop for brief Rule 3 / §11.4: audit logs, published scheme versions and generated
    /// obligations/rounds cannot be deleted, and audit logs / scheme versions cannot be updated.
    /// Corrections must be new, linked records.
    /// </summary>
    public partial class AppendOnlyGuards : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION ajo_reject_change() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'ajo_append_only: % on % is not permitted', TG_OP, TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER audit_logs_append_only BEFORE UPDATE OR DELETE ON audit_logs
                    FOR EACH ROW EXECUTE FUNCTION ajo_reject_change();
                CREATE TRIGGER scheme_versions_immutable BEFORE UPDATE OR DELETE ON scheme_versions
                    FOR EACH ROW EXECUTE FUNCTION ajo_reject_change();
                CREATE TRIGGER contribution_obligations_no_delete BEFORE DELETE ON contribution_obligations
                    FOR EACH ROW EXECUTE FUNCTION ajo_reject_change();
                CREATE TRIGGER scheme_rounds_no_delete BEFORE DELETE ON scheme_rounds
                    FOR EACH ROW EXECUTE FUNCTION ajo_reject_change();
                CREATE TRIGGER payouts_no_delete BEFORE DELETE ON payouts
                    FOR EACH ROW EXECUTE FUNCTION ajo_reject_change();
                CREATE TRIGGER group_memberships_no_delete BEFORE DELETE ON group_memberships
                    FOR EACH ROW EXECUTE FUNCTION ajo_reject_change();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS group_memberships_no_delete ON group_memberships;
                DROP TRIGGER IF EXISTS payouts_no_delete ON payouts;
                DROP TRIGGER IF EXISTS scheme_rounds_no_delete ON scheme_rounds;
                DROP TRIGGER IF EXISTS contribution_obligations_no_delete ON contribution_obligations;
                DROP TRIGGER IF EXISTS scheme_versions_immutable ON scheme_versions;
                DROP TRIGGER IF EXISTS audit_logs_append_only ON audit_logs;
                DROP FUNCTION IF EXISTS ajo_reject_change();
                """);
        }
    }
}
