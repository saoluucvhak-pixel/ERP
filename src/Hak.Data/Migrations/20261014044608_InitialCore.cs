using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hak.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_regimes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounting_regimes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    seq = table.Column<long>(type: "INTEGER", nullable: false),
                    at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    session_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    device = table.Column<string>(type: "TEXT", nullable: true),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    entity_type = table.Column<string>(type: "TEXT", nullable: false),
                    entity_id = table.Column<string>(type: "TEXT", nullable: true),
                    result = table.Column<string>(type: "TEXT", nullable: false),
                    entity_row_version = table.Column<long>(type: "INTEGER", nullable: true),
                    data_json = table.Column<string>(type: "TEXT", nullable: true),
                    prev_hash = table.Column<string>(type: "TEXT", nullable: false),
                    hash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    decimal_places = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.code);
                    table.CheckConstraint("ck_currencies_decimal_places", "decimal_places BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "exchange_partner_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    source_system = table.Column<string>(type: "TEXT", nullable: false),
                    key_id = table.Column<string>(type: "TEXT", nullable: false),
                    secret_base64 = table.Column<string>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_partner_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    package_id = table.Column<string>(type: "TEXT", nullable: false),
                    source_system = table.Column<string>(type: "TEXT", nullable: false),
                    document_type = table.Column<string>(type: "TEXT", nullable: false),
                    schema_version = table.Column<string>(type: "TEXT", nullable: false),
                    checksum = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    item_count = table.Column<int>(type: "INTEGER", nullable: false),
                    created_count = table.Column<int>(type: "INTEGER", nullable: false),
                    duplicate_count = table.Column<int>(type: "INTEGER", nullable: false),
                    error_count = table.Column<int>(type: "INTEGER", nullable: false),
                    result_json = table.Column<string>(type: "TEXT", nullable: true),
                    received_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    received_by = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_name = table.Column<string>(type: "TEXT", nullable: false),
                    normalized_user_name = table.Column<string>(type: "TEXT", nullable: false),
                    full_name = table.Column<string>(type: "TEXT", nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_system_admin = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "voucher_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    from_status = table.Column<int>(type: "INTEGER", nullable: true),
                    to_status = table.Column<int>(type: "INTEGER", nullable: false),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    voucher_row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "voucher_number_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    fiscal_year_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    voucher_type = table.Column<string>(type: "TEXT", nullable: false),
                    last_number = table.Column<long>(type: "INTEGER", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_number_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounting_regime_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    regime_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    version_code = table.Column<string>(type: "TEXT", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    verification_status = table.Column<int>(type: "INTEGER", nullable: false),
                    verified_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    verified_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    verification_note = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    permission = table.Column<string>(type: "TEXT", nullable: false)
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
                name: "login_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", nullable: false),
                    device = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    tax_code = table.Column<string>(type: "TEXT", nullable: true),
                    functional_currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    regime_version_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    require_distinct_approver = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    regime_version_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    rule_group = table.Column<string>(type: "TEXT", nullable: false),
                    document_no = table.Column<string>(type: "TEXT", nullable: false),
                    issued_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    article = table.Column<string>(type: "TEXT", nullable: true),
                    url = table.Column<string>(type: "TEXT", nullable: true),
                    copy_sha256 = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    parent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    is_postable = table.Column<bool>(type: "INTEGER", nullable: false),
                    requires_partner = table.Column<bool>(type: "INTEGER", nullable: false),
                    balance_display = table.Column<int>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    regime_version_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    parent_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    tax_code = table.Column<string>(type: "TEXT", nullable: true),
                    roles = table.Column<int>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", nullable: false),
                    start_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    end_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false)
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
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    role_id = table.Column<Guid>(type: "TEXT", nullable: false)
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
                name: "accounting_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    fiscal_year_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_no = table.Column<int>(type: "INTEGER", nullable: false),
                    start_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    end_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    locked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    locked_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    branch_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    voucher_type = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    number = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    fiscal_year_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    voucher_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    currency_code = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    source_system = table.Column<string>(type: "TEXT", nullable: true),
                    source_id = table.Column<string>(type: "TEXT", nullable: true),
                    source_version = table.Column<string>(type: "TEXT", nullable: true),
                    reverses_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    adjusts_voucher_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    submitted_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    approved_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    posted_by = table.Column<Guid>(type: "TEXT", nullable: true),
                    posted_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false)
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
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    period_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    regime_version_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    voucher_type = table.Column<string>(type: "TEXT", nullable: false),
                    voucher_number = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    posted_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    posted_by = table.Column<Guid>(type: "TEXT", nullable: false)
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
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    side = table.Column<int>(type: "INTEGER", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    foreign_currency = table.Column<string>(type: "TEXT", nullable: true),
                    foreign_amount = table.Column<long>(type: "INTEGER", nullable: true),
                    exchange_rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "journal_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    line_no = table.Column<int>(type: "INTEGER", nullable: false),
                    account_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    partner_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    side = table.Column<int>(type: "INTEGER", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    foreign_currency = table.Column<string>(type: "TEXT", nullable: true),
                    foreign_amount = table.Column<long>(type: "INTEGER", nullable: true),
                    exchange_rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    posting_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "ix_roles_code",
                table: "roles",
                column: "code",
                unique: true);

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
                name: "ix_voucher_number_sequences_company_id_fiscal_year_id_voucher_type",
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
                filter: "source_id IS NOT NULL");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropTable(
                name: "exchange_partner_keys");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "legal_sources");

            migrationBuilder.DropTable(
                name: "login_sessions");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_company_roles");

            migrationBuilder.DropTable(
                name: "voucher_history");

            migrationBuilder.DropTable(
                name: "voucher_lines");

            migrationBuilder.DropTable(
                name: "voucher_number_sequences");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "business_partners");

            migrationBuilder.DropTable(
                name: "accounting_periods");

            migrationBuilder.DropTable(
                name: "vouchers");

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
