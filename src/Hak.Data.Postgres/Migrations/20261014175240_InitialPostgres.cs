using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_regimes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_regimes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device = table.Column<string>(type: "text", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: true),
                    result = table.Column<string>(type: "text", nullable: false),
                    entity_row_version = table.Column<long>(type: "bigint", nullable: true),
                    data_json = table.Column<string>(type: "text", nullable: true),
                    prev_hash = table.Column<string>(type: "text", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "closing_rule_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_closing_rule_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "costing_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costing_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    decimal_places = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.code);
                    table.CheckConstraint("ck_currencies_decimal_places", "decimal_places BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "dryness_methods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    dryness_decimals = table.Column<int>(type: "integer", nullable: false),
                    bdmt_decimals = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    approval_note = table.Column<string>(type: "text", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dryness_methods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_name = table.Column<string>(type: "text", nullable: false),
                    source_sha256 = table.Column<string>(type: "text", nullable: false),
                    imported = table.Column<int>(type: "integer", nullable: false),
                    duplicates = table.Column<int>(type: "integer", nullable: false),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    imported_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_partner_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "text", nullable: false),
                    key_id = table.Column<string>(type: "text", nullable: false),
                    secret_base64 = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_partner_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<string>(type: "text", nullable: false),
                    source_system = table.Column<string>(type: "text", nullable: false),
                    document_type = table.Column<string>(type: "text", nullable: false),
                    schema_version = table.Column<string>(type: "text", nullable: false),
                    checksum = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    created_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    received_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "posting_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    document_type = table.Column<string>(type: "text", nullable: false),
                    business_event = table.Column<string>(type: "text", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    definition_json = table.Column<string>(type: "text", nullable: false),
                    test_cases_json = table.Column<string>(type: "text", nullable: false),
                    last_test_run_json = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    regime_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    activated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posting_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tax_declarations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_type = table.Column<string>(type: "text", nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    filing_reference = table.Column<string>(type: "text", nullable: true),
                    filed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_declarations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "text", nullable: false),
                    normalized_user_name = table.Column<string>(type: "text", nullable: false),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_system_admin = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "voucher_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    voucher_row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "voucher_number_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_type = table.Column<string>(type: "text", nullable: false),
                    last_number = table.Column<long>(type: "bigint", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_number_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_regime_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    regime_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_code = table.Column<string>(type: "text", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    verification_status = table.Column<int>(type: "integer", nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    verification_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_regime_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_accounting_regime_versions_accounting_regimes_regime_id",
                        column: x => x.regime_id,
                        principalTable: "accounting_regimes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    seller_tax_code = table.Column<string>(type: "text", nullable: false),
                    seller_name = table.Column<string>(type: "text", nullable: false),
                    buyer_tax_code = table.Column<string>(type: "text", nullable: true),
                    buyer_name = table.Column<string>(type: "text", nullable: true),
                    series = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: true),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    amount_before_vat = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_payment = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    related_series = table.Column<string>(type: "text", nullable: true),
                    related_number = table.Column<string>(type: "text", nullable: true),
                    lookup_code = table.Column<string>(type: "text", nullable: true),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    imported_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_e_invoice_records_e_invoice_import_batches_import_batch_id",
                        column: x => x.import_batch_id,
                        principalTable: "e_invoice_import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission });
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_declaration_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    declaration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    indicator_code = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    declared_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    measure = table.Column<int>(type: "integer", nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_declaration_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_tax_declaration_lines_tax_declarations_declaration_id",
                        column: x => x.declaration_id,
                        principalTable: "tax_declarations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "login_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    device = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_login_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    tax_code = table.Column<string>(type: "text", nullable: true),
                    functional_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    regime_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    require_distinct_approver = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_companies_accounting_regime_versions_regime_version_id",
                        column: x => x.regime_version_id,
                        principalTable: "accounting_regime_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companies_currencies_functional_currency",
                        column: x => x.functional_currency,
                        principalTable: "currencies",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "legal_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    regime_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_group = table.Column<string>(type: "text", nullable: false),
                    document_no = table.Column<string>(type: "text", nullable: false),
                    issued_date = table.Column<DateOnly>(type: "date", nullable: true),
                    article = table.Column<string>(type: "text", nullable: true),
                    url = table.Column<string>(type: "text", nullable: true),
                    copy_sha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_sources", x => x.id);
                    table.ForeignKey(
                        name: "fk_legal_sources_accounting_regime_versions_regime_version_id",
                        column: x => x.regime_version_id,
                        principalTable: "accounting_regime_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_rate_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    e_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    amount_before_vat = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_rate_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_e_invoice_rate_lines_e_invoice_records_e_invoice_id",
                        column: x => x.e_invoice_id,
                        principalTable: "e_invoice_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "e_invoice_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    e_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    new_status = table.Column<int>(type: "integer", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_e_invoice_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_e_invoice_status_changes_e_invoice_records_e_invoice_id",
                        column: x => x.e_invoice_id,
                        principalTable: "e_invoice_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_postable = table.Column<bool>(type: "boolean", nullable: false),
                    requires_partner = table.Column<bool>(type: "boolean", nullable: false),
                    balance_display = table.Column<int>(type: "integer", nullable: false),
                    open_item_nature = table.Column<int>(type: "integer", nullable: false),
                    cash_kind = table.Column<int>(type: "integer", nullable: false),
                    negative_balance_policy = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    regime_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_accounts_accounts_parent_id",
                        column: x => x.parent_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_accounts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                    table.ForeignKey(
                        name: "fk_branches_branches_parent_id",
                        column: x => x.parent_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_branches_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_partners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    tax_code = table.Column<string>(type: "text", nullable: true),
                    roles = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_partners", x => x.id);
                    table.ForeignKey(
                        name: "fk_business_partners_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_years",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_years", x => x.id);
                    table.CheckConstraint("ck_fiscal_years_dates", "end_date >= start_date");
                    table.ForeignKey(
                        name: "fk_fiscal_years_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_company_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_company_roles", x => new { x.user_id, x.company_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_company_roles_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_company_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_company_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                    table.ForeignKey(
                        name: "fk_warehouses_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_role_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_code = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_role_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_role_mappings_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    closing_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    source_name = table.Column<string>(type: "text", nullable: false),
                    source_sha256 = table.Column<string>(type: "text", nullable: false),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    imported_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statements", x => x.id);
                    table.ForeignKey(
                        name: "fk_bank_statements_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "closing_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    source_prefixes = table.Column<string>(type: "text", nullable: false),
                    target_account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_closing_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_closing_steps_accounts_target_account_id",
                        column: x => x.target_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_closing_steps_closing_rule_sets_rule_set_id",
                        column: x => x.rule_set_id,
                        principalTable: "closing_rule_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    unit = table.Column<string>(type: "text", nullable: false),
                    inventory_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracks_dryness = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_items_accounts_inventory_account_id",
                        column: x => x.inventory_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "commercial_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    adjusts_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number = table.Column<string>(type: "text", nullable: false),
                    invoice_series = table.Column<string>(type: "text", nullable: false),
                    invoice_no = table.Column<string>(type: "text", nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: true),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commercial_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_commercial_invoices_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_commercial_invoices_commercial_invoices_adjusts_invoice_id",
                        column: x => x.adjusts_invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_event = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    requested_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    beneficiary_account_no = table.Column<string>(type: "text", nullable: true),
                    beneficiary_bank = table.Column<string>(type: "text", nullable: true),
                    settle_journal_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_by = table.Column<Guid>(type: "uuid", nullable: true),
                    executed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    bank_reference = table.Column<string>(type: "text", nullable: true),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_orders", x => x.id);
                    table.CheckConstraint("ck_payment_orders_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payment_orders_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_orders_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trade_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_no = table.Column<string>(type: "text", nullable: true),
                    order_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_date = table.Column<DateOnly>(type: "date", nullable: true),
                    currency = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    over_delivery_tolerance_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    status_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_orders", x => x.id);
                    table.CheckConstraint("ck_trade_orders_tolerance", "over_delivery_tolerance_percent >= 0 AND over_delivery_tolerance_percent <= 100");
                    table.ForeignKey(
                        name: "fk_trade_orders_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_no = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_periods", x => x.id);
                    table.ForeignKey(
                        name: "fk_accounting_periods_fiscal_years_fiscal_year_id",
                        column: x => x.fiscal_year_id,
                        principalTable: "fiscal_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vouchers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voucher_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    fiscal_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    currency_code = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    source_system = table.Column<string>(type: "text", nullable: true),
                    source_id = table.Column<string>(type: "text", nullable: true),
                    source_version = table.Column<string>(type: "text", nullable: true),
                    reverses_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    adjusts_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    business_event = table.Column<string>(type: "text", nullable: true),
                    posting_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    posting_rule_version = table.Column<int>(type: "integer", nullable: true),
                    main_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vouchers", x => x.id);
                    table.ForeignKey(
                        name: "fk_vouchers_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vouchers_fiscal_years_fiscal_year_id",
                        column: x => x.fiscal_year_id,
                        principalTable: "fiscal_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vouchers_vouchers_adjusts_voucher_id",
                        column: x => x.adjusts_voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vouchers_vouchers_reverses_voucher_id",
                        column: x => x.reverses_voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "costing_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_costing_policies", x => x.id);
                    table.ForeignKey(
                        name: "fk_costing_policies_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reverses_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_documents_warehouses_to_warehouse_id",
                        column: x => x.to_warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_documents_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    transaction_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    bank_reference = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    counterparty_name = table.Column<string>(type: "text", nullable: true),
                    counterparty_account = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    candidates_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_lines", x => x.id);
                    table.CheckConstraint("ck_bank_statement_lines_amount", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_bank_statement_lines_bank_statements_statement_id",
                        column: x => x.statement_id,
                        principalTable: "bank_statements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    movement_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    counter_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reverses_movement_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_quantity", "quantity <> 0");
                    table.ForeignKey(
                        name: "fk_stock_movements_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "commercial_invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    adjusts_line_no = table.Column<int>(type: "integer", nullable: true),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_line_no = table.Column<int>(type: "integer", nullable: true),
                    stock_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    basis_quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    functional_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_line_no = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commercial_invoice_lines", x => x.id);
                    table.CheckConstraint("ck_commercial_invoice_lines_amount", "amount >= 0 AND vat_amount >= 0");
                    table.ForeignKey(
                        name: "fk_commercial_invoice_lines_commercial_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "commercial_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trade_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    basis = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_order_lines", x => x.id);
                    table.CheckConstraint("ck_trade_order_lines_quantity", "quantity > 0 AND unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_trade_order_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trade_order_lines_trade_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "trade_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    regime_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    voucher_type = table.Column<string>(type: "text", nullable: false),
                    voucher_number = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_entries_accounting_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "accounting_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_accounting_regime_versions_regime_version_id",
                        column: x => x.regime_version_id,
                        principalTable: "accounting_regime_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "voucher_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    side = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    foreign_currency = table.Column<string>(type: "text", nullable: true),
                    foreign_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    settle_journal_line_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_lines", x => x.id);
                    table.CheckConstraint("ck_voucher_lines_amount", "amount > 0");
                    table.CheckConstraint("ck_voucher_lines_side", "side IN (1, -1)");
                    table.ForeignKey(
                        name: "fk_voucher_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_voucher_lines_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_voucher_lines_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_document_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    counter_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_line_no = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_document_lines", x => x.id);
                    table.CheckConstraint("ck_stock_document_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_stock_document_lines_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_document_lines_stock_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_measurements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    measured_mt = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    dryness_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    bdmt = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    method_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_type = table.Column<int>(type: "integer", nullable: false),
                    evidence_no = table.Column<string>(type: "text", nullable: false),
                    evidence_issuer = table.Column<string>(type: "text", nullable: true),
                    evidence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    superseded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    superseded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    supersede_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_measurements", x => x.id);
                    table.CheckConstraint("ck_stock_measurements_dryness", "dryness_percent > 0 AND dryness_percent <= 100");
                    table.CheckConstraint("ck_stock_measurements_mt", "measured_mt > 0");
                    table.ForeignKey(
                        name: "fk_stock_measurements_dryness_methods_method_id",
                        column: x => x.method_id,
                        principalTable: "dryness_methods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_measurements_stock_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_valuations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    costing_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_valuations", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_valuations_stock_movements_movement_id",
                        column: x => x.movement_id,
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_value_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    adjustment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    source_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_value_adjustments", x => x.id);
                    table.CheckConstraint("ck_stock_value_adjustments_value", "value <> 0");
                    table.ForeignKey(
                        name: "fk_stock_value_adjustments_stock_movements_target_movement_id",
                        column: x => x.target_movement_id,
                        principalTable: "stock_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    side = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    foreign_currency = table.Column<string>(type: "text", nullable: true),
                    foreign_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    exchange_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_lines", x => x.id);
                    table.CheckConstraint("ck_journal_lines_amount", "amount > 0");
                    table.CheckConstraint("ck_journal_lines_side", "side IN (1, -1)");
                    table.ForeignKey(
                        name: "fk_journal_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_lines_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_lines_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_matches", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliation_matches_bank_statement_lines_statement_line_~",
                        column: x => x.statement_line_id,
                        principalTable: "bank_statement_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reconciliation_matches_journal_lines_journal_line_id",
                        column: x => x.journal_line_id,
                        principalTable: "journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debit_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    allocation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement_allocations", x => x.id);
                    table.CheckConstraint("ck_settlement_allocations_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_settlement_allocations_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_allocations_business_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "business_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_allocations_journal_lines_credit_line_id",
                        column: x => x.credit_line_id,
                        principalTable: "journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_allocations_journal_lines_debit_line_id",
                        column: x => x.debit_line_id,
                        principalTable: "journal_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_role_mappings_account_id",
                table: "account_role_mappings",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_role_mappings_company_id_role_code",
                table: "account_role_mappings",
                columns: new[] { "company_id", "role_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounting_periods_company_id_fiscal_year_id_period_no",
                table: "accounting_periods",
                columns: new[] { "company_id", "fiscal_year_id", "period_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounting_periods_company_id_start_date",
                table: "accounting_periods",
                columns: new[] { "company_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_accounting_periods_fiscal_year_id",
                table: "accounting_periods",
                column: "fiscal_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounting_regime_versions_regime_id_version_code",
                table: "accounting_regime_versions",
                columns: new[] { "regime_id", "version_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounting_regimes_code",
                table: "accounting_regimes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_company_id_code",
                table: "accounts",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_parent_id",
                table: "accounts",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_company_id_seq",
                table: "audit_logs",
                columns: new[] { "company_id", "seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_company_id_bank_account_id_bank_refere~",
                table: "bank_statement_lines",
                columns: new[] { "company_id", "bank_account_id", "bank_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_company_id_bank_account_id_status",
                table: "bank_statement_lines",
                columns: new[] { "company_id", "bank_account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_lines_statement_id",
                table: "bank_statement_lines",
                column: "statement_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statements_bank_account_id",
                table: "bank_statements",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_statements_company_id_bank_account_id_source_sha256",
                table: "bank_statements",
                columns: new[] { "company_id", "bank_account_id", "source_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branches_company_id_code",
                table: "branches",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branches_parent_id",
                table: "branches",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_partners_company_id_code",
                table: "business_partners",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_partners_company_id_tax_code",
                table: "business_partners",
                columns: new[] { "company_id", "tax_code" });

            migrationBuilder.CreateIndex(
                name: "ix_closing_rule_sets_company_id_code_version",
                table: "closing_rule_sets",
                columns: new[] { "company_id", "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_closing_steps_rule_set_id",
                table: "closing_steps",
                column: "rule_set_id");

            migrationBuilder.CreateIndex(
                name: "ix_closing_steps_target_account_id",
                table: "closing_steps",
                column: "target_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoice_lines_company_id_stock_document_id_stock~",
                table: "commercial_invoice_lines",
                columns: new[] { "company_id", "stock_document_id", "stock_line_no" });

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoice_lines_invoice_id_line_no",
                table: "commercial_invoice_lines",
                columns: new[] { "invoice_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_adjusts_invoice_id",
                table: "commercial_invoices",
                column: "adjusts_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_company_id_adjusts_invoice_id",
                table: "commercial_invoices",
                columns: new[] { "company_id", "adjusts_invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_company_id_kind_partner_id_invoice_seri~",
                table: "commercial_invoices",
                columns: new[] { "company_id", "kind", "partner_id", "invoice_series", "invoice_no" });

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_company_id_number",
                table: "commercial_invoices",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commercial_invoices_partner_id",
                table: "commercial_invoices",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_code",
                table: "companies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_companies_functional_currency",
                table: "companies",
                column: "functional_currency");

            migrationBuilder.CreateIndex(
                name: "ix_companies_regime_version_id",
                table: "companies",
                column: "regime_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_costing_policies_company_id_warehouse_id_effective_from",
                table: "costing_policies",
                columns: new[] { "company_id", "warehouse_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_costing_policies_warehouse_id",
                table: "costing_policies",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_costing_runs_company_id_warehouse_id_period_id",
                table: "costing_runs",
                columns: new[] { "company_id", "warehouse_id", "period_id" },
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_dryness_methods_company_id_code",
                table: "dryness_methods",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_import_batches_company_id_source_sha256",
                table: "e_invoice_import_batches",
                columns: new[] { "company_id", "source_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_rate_lines_e_invoice_id",
                table: "e_invoice_rate_lines",
                column: "e_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_records_company_id_direction_seller_tax_code_seri~",
                table: "e_invoice_records",
                columns: new[] { "company_id", "direction", "seller_tax_code", "series", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_records_company_id_issue_date",
                table: "e_invoice_records",
                columns: new[] { "company_id", "issue_date" });

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_records_import_batch_id",
                table: "e_invoice_records",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_e_invoice_status_changes_e_invoice_id_at",
                table: "e_invoice_status_changes",
                columns: new[] { "e_invoice_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_exchange_partner_keys_company_id_source_system_key_id",
                table: "exchange_partner_keys",
                columns: new[] { "company_id", "source_system", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_years_company_id_code",
                table: "fiscal_years",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_company_id_package_id",
                table: "import_batches",
                columns: new[] { "company_id", "package_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_company_id_code",
                table: "items",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_inventory_account_id",
                table: "items",
                column: "inventory_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_company_id_posting_date",
                table: "journal_entries",
                columns: new[] { "company_id", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_company_id_voucher_id",
                table: "journal_entries",
                columns: new[] { "company_id", "voucher_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_period_id",
                table: "journal_entries",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_regime_version_id",
                table: "journal_entries",
                column: "regime_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_voucher_id",
                table: "journal_entries",
                column: "voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_account_id",
                table: "journal_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_company_id_account_id_posting_date",
                table: "journal_lines",
                columns: new[] { "company_id", "account_id", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_company_id_partner_id_account_id",
                table: "journal_lines",
                columns: new[] { "company_id", "partner_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_journal_entry_id",
                table: "journal_lines",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_partner_id",
                table: "journal_lines",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_legal_sources_regime_version_id",
                table: "legal_sources",
                column: "regime_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_login_sessions_token_hash",
                table: "login_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_login_sessions_user_id",
                table: "login_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_bank_account_id",
                table: "payment_orders",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_company_id_number",
                table: "payment_orders",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_company_id_status",
                table: "payment_orders",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_company_id_voucher_id",
                table: "payment_orders",
                columns: new[] { "company_id", "voucher_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_partner_id",
                table: "payment_orders",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_posting_rules_company_id_code_version",
                table: "posting_rules",
                columns: new[] { "company_id", "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_posting_rules_company_id_document_type_business_event_status",
                table: "posting_rules",
                columns: new[] { "company_id", "document_type", "business_event", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_matches_company_id_journal_line_id",
                table: "reconciliation_matches",
                columns: new[] { "company_id", "journal_line_id" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_matches_company_id_statement_line_id",
                table: "reconciliation_matches",
                columns: new[] { "company_id", "statement_line_id" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_matches_journal_line_id",
                table: "reconciliation_matches",
                column: "journal_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_matches_statement_line_id",
                table: "reconciliation_matches",
                column: "statement_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_code",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_account_id",
                table: "settlement_allocations",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_company_id_account_id_partner_id",
                table: "settlement_allocations",
                columns: new[] { "company_id", "account_id", "partner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_company_id_credit_line_id",
                table: "settlement_allocations",
                columns: new[] { "company_id", "credit_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_company_id_debit_line_id",
                table: "settlement_allocations",
                columns: new[] { "company_id", "debit_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_credit_line_id",
                table: "settlement_allocations",
                column: "credit_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_debit_line_id",
                table: "settlement_allocations",
                column: "debit_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_allocations_partner_id",
                table: "settlement_allocations",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_company_id_order_id_order_line_no",
                table: "stock_document_lines",
                columns: new[] { "company_id", "order_id", "order_line_no" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_document_id_line_no",
                table: "stock_document_lines",
                columns: new[] { "document_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_item_id",
                table: "stock_document_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_company_id_number",
                table: "stock_documents",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_company_id_reverses_document_id",
                table: "stock_documents",
                columns: new[] { "company_id", "reverses_document_id" },
                unique: true,
                filter: "reverses_document_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_to_warehouse_id",
                table: "stock_documents",
                column: "to_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_warehouse_id",
                table: "stock_documents",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_measurements_company_id_document_id_line_no",
                table: "stock_measurements",
                columns: new[] { "company_id", "document_id", "line_no" },
                unique: true,
                filter: "superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_measurements_company_id_evidence_no",
                table: "stock_measurements",
                columns: new[] { "company_id", "evidence_no" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_measurements_document_id",
                table: "stock_measurements",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_measurements_method_id",
                table: "stock_measurements",
                column: "method_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_company_id_document_id",
                table: "stock_movements",
                columns: new[] { "company_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_company_id_sequence",
                table: "stock_movements",
                columns: new[] { "company_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_company_id_warehouse_id_item_id_sequence",
                table: "stock_movements",
                columns: new[] { "company_id", "warehouse_id", "item_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_item_id",
                table: "stock_movements",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_warehouse_id",
                table: "stock_movements",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_valuations_company_id_movement_id",
                table: "stock_valuations",
                columns: new[] { "company_id", "movement_id" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_valuations_movement_id",
                table: "stock_valuations",
                column: "movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_value_adjustments_company_id_sequence",
                table: "stock_value_adjustments",
                columns: new[] { "company_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_value_adjustments_company_id_warehouse_id_item_id_seq~",
                table: "stock_value_adjustments",
                columns: new[] { "company_id", "warehouse_id", "item_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_value_adjustments_target_movement_id",
                table: "stock_value_adjustments",
                column: "target_movement_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_declaration_lines_declaration_id_indicator_code",
                table: "tax_declaration_lines",
                columns: new[] { "declaration_id", "indicator_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_declarations_company_id_tax_type_period_from_period_to",
                table: "tax_declarations",
                columns: new[] { "company_id", "tax_type", "period_from", "period_to" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trade_order_lines_item_id",
                table: "trade_order_lines",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_trade_order_lines_order_id_line_no",
                table: "trade_order_lines",
                columns: new[] { "order_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trade_orders_company_id_kind_status",
                table: "trade_orders",
                columns: new[] { "company_id", "kind", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_trade_orders_company_id_number",
                table: "trade_orders",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trade_orders_partner_id",
                table: "trade_orders",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_company_roles_company_id",
                table: "user_company_roles",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_company_roles_role_id",
                table: "user_company_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_voucher_history_company_id_voucher_id_at",
                table: "voucher_history",
                columns: new[] { "company_id", "voucher_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_lines_account_id",
                table: "voucher_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_lines_partner_id",
                table: "voucher_lines",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_voucher_lines_voucher_id_line_no",
                table: "voucher_lines",
                columns: new[] { "voucher_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_voucher_number_sequences_company_id_fiscal_year_id_voucher_~",
                table: "voucher_number_sequences",
                columns: new[] { "company_id", "fiscal_year_id", "voucher_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_adjusts_voucher_id",
                table: "vouchers",
                column: "adjusts_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_company_id_reverses_voucher_id",
                table: "vouchers",
                columns: new[] { "company_id", "reverses_voucher_id" },
                unique: true,
                filter: "reverses_voucher_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_company_id_source_system_source_id",
                table: "vouchers",
                columns: new[] { "company_id", "source_system", "source_id" },
                unique: true,
                filter: "source_id IS NOT NULL AND status <> 9");

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_company_id_status_posting_date",
                table: "vouchers",
                columns: new[] { "company_id", "status", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_company_id_voucher_type_fiscal_year_id_number",
                table: "vouchers",
                columns: new[] { "company_id", "voucher_type", "fiscal_year_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_fiscal_year_id",
                table: "vouchers",
                column: "fiscal_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_reverses_voucher_id",
                table: "vouchers",
                column: "reverses_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_company_id_code",
                table: "warehouses",
                columns: new[] { "company_id", "code" },
                unique: true);

            foreach (var sql in ImmutabilityTriggers.Statements()) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in ImmutabilityTriggers.DownStatements()) migrationBuilder.Sql(sql);
            migrationBuilder.DropTable(
                name: "account_role_mappings");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropTable(
                name: "closing_steps");

            migrationBuilder.DropTable(
                name: "commercial_invoice_lines");

            migrationBuilder.DropTable(
                name: "costing_policies");

            migrationBuilder.DropTable(
                name: "costing_runs");

            migrationBuilder.DropTable(
                name: "e_invoice_rate_lines");

            migrationBuilder.DropTable(
                name: "e_invoice_status_changes");

            migrationBuilder.DropTable(
                name: "exchange_partner_keys");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropTable(
                name: "legal_sources");

            migrationBuilder.DropTable(
                name: "login_sessions");

            migrationBuilder.DropTable(
                name: "payment_orders");

            migrationBuilder.DropTable(
                name: "posting_rules");

            migrationBuilder.DropTable(
                name: "reconciliation_matches");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "settlement_allocations");

            migrationBuilder.DropTable(
                name: "stock_document_lines");

            migrationBuilder.DropTable(
                name: "stock_measurements");

            migrationBuilder.DropTable(
                name: "stock_valuations");

            migrationBuilder.DropTable(
                name: "stock_value_adjustments");

            migrationBuilder.DropTable(
                name: "tax_declaration_lines");

            migrationBuilder.DropTable(
                name: "trade_order_lines");

            migrationBuilder.DropTable(
                name: "user_company_roles");

            migrationBuilder.DropTable(
                name: "voucher_history");

            migrationBuilder.DropTable(
                name: "voucher_lines");

            migrationBuilder.DropTable(
                name: "voucher_number_sequences");

            migrationBuilder.DropTable(
                name: "closing_rule_sets");

            migrationBuilder.DropTable(
                name: "commercial_invoices");

            migrationBuilder.DropTable(
                name: "e_invoice_records");

            migrationBuilder.DropTable(
                name: "bank_statement_lines");

            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "dryness_methods");

            migrationBuilder.DropTable(
                name: "stock_documents");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "tax_declarations");

            migrationBuilder.DropTable(
                name: "trade_orders");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "e_invoice_import_batches");

            migrationBuilder.DropTable(
                name: "bank_statements");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "warehouses");

            migrationBuilder.DropTable(
                name: "business_partners");

            migrationBuilder.DropTable(
                name: "accounting_periods");

            migrationBuilder.DropTable(
                name: "vouchers");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "fiscal_years");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "accounting_regime_versions");

            migrationBuilder.DropTable(
                name: "currencies");

            migrationBuilder.DropTable(
                name: "accounting_regimes");
        }
    }
}
