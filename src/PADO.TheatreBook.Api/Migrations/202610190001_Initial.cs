using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PADO.TheatreBook.Api.Migrations;

[DbContext(typeof(TheatreDb))]
[Migration("202610190001_Initial")]
public sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE EXTENSION IF NOT EXISTS btree_gist;
            CREATE TABLE "Users" (
                "Id" uuid PRIMARY KEY, "Email" varchar(254) NOT NULL,
                "PasswordHash" text NOT NULL, "Role" varchar(32) NOT NULL);
            CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

            CREATE TABLE "Halls" ("Id" uuid PRIMARY KEY, "Name" varchar(120) NOT NULL);
            CREATE UNIQUE INDEX "IX_Halls_Name" ON "Halls" ("Name");

            CREATE TABLE "Productions" (
                "Id" uuid PRIMARY KEY, "Title" varchar(200) NOT NULL,
                "Description" varchar(2000) NOT NULL, "DurationMinutes" integer NOT NULL,
                CONSTRAINT "CK_Production_Duration" CHECK ("DurationMinutes" BETWEEN 1 AND 1440));

            CREATE TABLE "Seats" (
                "Id" uuid PRIMARY KEY, "HallId" uuid NOT NULL REFERENCES "Halls" ("Id"),
                "RowLabel" varchar(4) NOT NULL, "Number" integer NOT NULL,
                CONSTRAINT "CK_Seat_Number" CHECK ("Number" > 0));
            CREATE UNIQUE INDEX "IX_Seats_HallId_RowLabel_Number"
                ON "Seats" ("HallId", "RowLabel", "Number");

            CREATE TABLE "Performances" (
                "Id" uuid PRIMARY KEY,
                "HallId" uuid NOT NULL REFERENCES "Halls" ("Id"),
                "ProductionId" uuid NOT NULL REFERENCES "Productions" ("Id"),
                "StartsAtUtc" timestamptz NOT NULL,
                "EndsAtUtc" timestamptz NOT NULL,
                "PriceUah" numeric(12,2) NOT NULL,
                "IsCancelled" boolean NOT NULL DEFAULT false,
                CONSTRAINT "CK_Performance_Interval" CHECK ("EndsAtUtc" > "StartsAtUtc"),
                CONSTRAINT "CK_Performance_Price" CHECK ("PriceUah" >= 0));
            CREATE INDEX "IX_Performances_HallId_StartsAtUtc"
                ON "Performances" ("HallId", "StartsAtUtc");
            ALTER TABLE "Performances" ADD CONSTRAINT "EX_Performances_HallTime"
                EXCLUDE USING gist (
                    "HallId" WITH =,
                    tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&
                ) WHERE ("IsCancelled" = false);

            CREATE TABLE "Reservations" (
                "Id" uuid PRIMARY KEY,
                "PerformanceId" uuid NOT NULL REFERENCES "Performances" ("Id"),
                "SeatId" uuid NOT NULL REFERENCES "Seats" ("Id"),
                "CustomerId" uuid NOT NULL REFERENCES "Users" ("Id"),
                "Status" integer NOT NULL,
                "CreatedAtUtc" timestamptz NOT NULL,
                "PriceAtBookingUah" numeric(12,2) NOT NULL,
                CONSTRAINT "CK_Reservation_Status" CHECK ("Status" IN (0,1,2)));
            CREATE UNIQUE INDEX "IX_Reservations_PerformanceId_SeatId"
                ON "Reservations" ("PerformanceId", "SeatId")
                WHERE "Status" IN (0, 1);
            CREATE INDEX "IX_Reservations_CustomerId" ON "Reservations" ("CustomerId");

            CREATE TABLE "Tickets" (
                "Id" uuid PRIMARY KEY,
                "ReservationId" uuid NOT NULL UNIQUE REFERENCES "Reservations" ("Id"),
                "IssuedAtUtc" timestamptz NOT NULL);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "Tickets";
            DROP TABLE "Reservations";
            DROP TABLE "Performances";
            DROP TABLE "Seats";
            DROP TABLE "Productions";
            DROP TABLE "Halls";
            DROP TABLE "Users";
            """);
    }
}
