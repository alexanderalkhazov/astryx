using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-written: EF cannot create partitioned tables. Columns, keys and indexes match the model snapshot.
            migrationBuilder.EnsureSchema(name: "audit");

            migrationBuilder.Sql("""
                CREATE TABLE audit.audit_logs (
                    id uuid NOT NULL,
                    occurred_at timestamp with time zone NOT NULL,
                    actor_type character varying(16) NOT NULL,
                    user_id uuid NULL,
                    action character varying(100) NOT NULL,
                    entity_type character varying(100) NULL,
                    entity_id character varying(100) NULL,
                    correlation_id character varying(64) NULL,
                    ip inet NULL,
                    user_agent character varying(256) NULL,
                    before jsonb NULL,
                    after jsonb NULL,
                    metadata jsonb NULL,
                    CONSTRAINT pk_audit_logs PRIMARY KEY (id, occurred_at),
                    CONSTRAINT ck_audit_logs_actor_type CHECK (actor_type IN ('user', 'system', 'worker', 'admin'))
                ) PARTITION BY RANGE (occurred_at);

                -- Catches rows for months without a partition, so an insert never fails.
                CREATE TABLE audit.audit_logs_default PARTITION OF audit.audit_logs DEFAULT;

                CREATE INDEX ix_audit_logs_entity ON audit.audit_logs (entity_type, entity_id);
                CREATE INDEX ix_audit_logs_user_occurred ON audit.audit_logs (user_id, occurred_at DESC);

                -- Monthly partition management; identifiers are quoted with %I, never concatenated from input.
                CREATE FUNCTION audit.ensure_month_partition(month_start date) RETURNS void
                LANGUAGE plpgsql AS $fn$
                DECLARE
                    first_day date := date_trunc('month', month_start)::date;
                    partition_name text := format('audit_logs_y%sm%s', to_char(first_day, 'YYYY'), to_char(first_day, 'MM'));
                BEGIN
                    EXECUTE format(
                        'CREATE TABLE IF NOT EXISTS audit.%I PARTITION OF audit.audit_logs FOR VALUES FROM (%L) TO (%L)',
                        partition_name, first_day::timestamptz, (first_day + interval '1 month')::timestamptz);
                END;
                $fn$;

                CREATE FUNCTION audit.drop_partitions_before(cutoff date) RETURNS integer
                LANGUAGE plpgsql AS $fn$
                DECLARE
                    part record;
                    dropped integer := 0;
                BEGIN
                    FOR part IN
                        SELECT c.relname
                        FROM pg_inherits i
                        JOIN pg_class c ON c.oid = i.inhrelid
                        JOIN pg_class p ON p.oid = i.inhparent
                        JOIN pg_namespace n ON n.oid = p.relnamespace
                        WHERE n.nspname = 'audit' AND p.relname = 'audit_logs' AND c.relname ~ '^audit_logs_y[0-9]{4}m[0-9]{2}$'
                          AND to_date(substr(c.relname, 13, 4) || substr(c.relname, 18, 2), 'YYYYMM') < date_trunc('month', cutoff)
                    LOOP
                        EXECUTE format('DROP TABLE audit.%I', part.relname);
                        dropped := dropped + 1;
                    END LOOP;
                    RETURN dropped;
                END;
                $fn$;

                -- Append-only: rows can be inserted and read, never changed or removed (SYSTEM_DESIGN.md §7.18).
                CREATE FUNCTION audit.reject_audit_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'audit.audit_logs is append-only (% is not allowed)', TG_OP USING ERRCODE = 'insufficient_privilege';
                END;
                $fn$;

                CREATE TRIGGER trg_audit_logs_append_only
                    BEFORE UPDATE OR DELETE ON audit.audit_logs
                    FOR EACH ROW EXECUTE FUNCTION audit.reject_audit_mutation();

                CREATE TRIGGER trg_audit_logs_no_truncate
                    BEFORE TRUNCATE ON audit.audit_logs
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_audit_mutation();

                SELECT audit.ensure_month_partition(current_date);
                SELECT audit.ensure_month_partition((current_date + interval '1 month')::date);
                SELECT audit.ensure_month_partition((current_date + interval '2 months')::date);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS audit.audit_logs CASCADE;
                DROP FUNCTION IF EXISTS audit.reject_audit_mutation();
                DROP FUNCTION IF EXISTS audit.drop_partitions_before(date);
                DROP FUNCTION IF EXISTS audit.ensure_month_partition(date);
                """);
        }
    }
}
