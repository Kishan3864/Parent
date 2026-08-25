using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ParentalTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email_normalized = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "child_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    device_label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    manufacturer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    os_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    app_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    install_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pairing_code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    pairing_code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_battery_percent = table.Column<int>(type: "integer", nullable: true),
                    last_location_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_child_devices", x => x.id);
                    table.ForeignKey(
                        name: "FK_child_devices_parents_parent_id",
                        column: x => x.parent_id,
                        principalTable: "parents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_parents_parent_id",
                        column: x => x.parent_id,
                        principalTable: "parents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    enrolled_user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_sessions_child_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "child_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "location_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    accuracy_meters = table.Column<double>(type: "double precision", nullable: false),
                    altitude_meters = table.Column<double>(type: "double precision", nullable: true),
                    speed_mps = table.Column<double>(type: "double precision", nullable: true),
                    bearing_degrees = table.Column<double>(type: "double precision", nullable: true),
                    battery_percent = table.Column<int>(type: "integer", nullable: true),
                    is_charging = table.Column<bool>(type: "boolean", nullable: true),
                    provider = table.Column<short>(type: "smallint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_location_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_location_records_child_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "child_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_child_devices_last_location_id",
                table: "child_devices",
                column: "last_location_id");

            migrationBuilder.CreateIndex(
                name: "ix_child_devices_parent_id",
                table: "child_devices",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_sessions_device_id",
                table: "device_sessions",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_location_records_device_id_client_id",
                table: "location_records",
                columns: new[] { "device_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_location_records_device_id_recorded_at",
                table: "location_records",
                columns: new[] { "device_id", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_parents_email_normalized",
                table: "parents",
                column: "email_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_parent_id",
                table: "refresh_tokens",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_sessions");

            migrationBuilder.DropTable(
                name: "location_records");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "child_devices");

            migrationBuilder.DropTable(
                name: "parents");
        }
    }
}
