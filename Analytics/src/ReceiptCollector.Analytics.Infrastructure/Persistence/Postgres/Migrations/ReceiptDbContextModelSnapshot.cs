using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Migrations;

[DbContext(typeof(ReceiptDbContext))]
public class ReceiptDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "8.0.7")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.CommodityEntity", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<int?>("CategoryId")
                .HasColumnType("integer");

            b.Property<string>("CategoryName")
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            b.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnType("character varying(256)");

            b.Property<int>("Nds")
                .HasColumnType("integer");

            b.Property<decimal>("NdsSum")
                .HasColumnType("numeric(18,2)");

            b.Property<Guid>("ReceiptId")
                .HasColumnType("uuid");

            b.Property<decimal>("Quantity")
                .HasColumnType("numeric(18,3)");

            b.Property<decimal>("UnitPrice")
                .HasColumnType("numeric(18,2)");

            b.HasKey("Id");

            b.HasIndex("ReceiptId");

            b.ToTable("commodities", (string)null);
        });

        modelBuilder.Entity("ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.ReceiptEntity", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<string>("ExternalId")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            b.Property<string>("Merchant")
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnType("character varying(256)");

            b.Property<DateTime>("PurchasedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<decimal>("TotalAmount")
                .HasColumnType("numeric(18,2)");

            b.Property<Guid>("UserId")
                .HasColumnType("uuid");

            b.HasKey("Id");

            b.ToTable("receipts", (string)null);
        });

        modelBuilder.Entity("ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.UserEntity", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid");

            b.Property<string>("ExternalId")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("character varying(128)");

            b.Property<string>("Name")
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnType("character varying(256)");

            b.HasKey("Id");

            b.HasIndex("ExternalId")
                .IsUnique();

            b.ToTable("users", (string)null);
        });

        modelBuilder.Entity("ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.CommodityEntity", b =>
        {
            b.HasOne("ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.ReceiptEntity", "Receipt")
                .WithMany("Items")
                .HasForeignKey("ReceiptId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            b.Navigation("Receipt");
        });

        modelBuilder.Entity("ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.ReceiptEntity", b =>
        {
            b.Navigation("Items");
        });
#pragma warning restore 612, 618
    }
}
