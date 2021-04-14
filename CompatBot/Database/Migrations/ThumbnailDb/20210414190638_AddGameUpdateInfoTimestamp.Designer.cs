﻿// <auto-generated />
using System;
using CompatBot.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CompatBot.Migrations
{
    [DbContext(typeof(ThumbnailDb))]
    [Migration("20210414190638_AddGameUpdateInfoTimestamp")]
    partial class AddGameUpdateInfoTimestamp
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "5.0.5");

            modelBuilder.Entity("CompatBot.Database.Fortune", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasColumnName("id");

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("TEXT")
                        .HasColumnName("content");

                    b.HasKey("Id")
                        .HasName("id");

                    b.ToTable("fortune");
                });

            modelBuilder.Entity("CompatBot.Database.GameUpdateInfo", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasColumnName("id");

                    b.Property<int>("MetaHash")
                        .HasColumnType("INTEGER")
                        .HasColumnName("meta_hash");

                    b.Property<string>("MetaXml")
                        .IsRequired()
                        .HasColumnType("TEXT")
                        .HasColumnName("meta_xml");

                    b.Property<string>("ProductCode")
                        .IsRequired()
                        .HasColumnType("TEXT")
                        .HasColumnName("product_code");

                    b.Property<long>("Timestamp")
                        .HasColumnType("INTEGER")
                        .HasColumnName("timestamp");

                    b.HasKey("Id")
                        .HasName("id");

                    b.HasIndex("ProductCode")
                        .IsUnique()
                        .HasDatabaseName("game_update_info_product_code");

                    b.ToTable("game_update_info");
                });

            modelBuilder.Entity("CompatBot.Database.Metacritic", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasColumnName("id");

                    b.Property<byte?>("CriticScore")
                        .HasColumnType("INTEGER")
                        .HasColumnName("critic_score");

                    b.Property<string>("Notes")
                        .HasColumnType("TEXT")
                        .HasColumnName("notes");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("TEXT")
                        .HasColumnName("title");

                    b.Property<byte?>("UserScore")
                        .HasColumnType("INTEGER")
                        .HasColumnName("user_score");

                    b.HasKey("Id")
                        .HasName("id");

                    b.ToTable("metacritic");
                });

            modelBuilder.Entity("CompatBot.Database.NamePool", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasColumnName("id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT")
                        .HasColumnName("name");

                    b.HasKey("Id")
                        .HasName("id");

                    b.ToTable("name_pool");
                });

            modelBuilder.Entity("CompatBot.Database.State", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasColumnName("id");

                    b.Property<string>("Locale")
                        .HasColumnType("TEXT")
                        .HasColumnName("locale");

                    b.Property<long>("Timestamp")
                        .HasColumnType("INTEGER")
                        .HasColumnName("timestamp");

                    b.HasKey("Id")
                        .HasName("id");

                    b.HasIndex("Locale")
                        .IsUnique()
                        .HasDatabaseName("state_locale");

                    b.HasIndex("Timestamp")
                        .HasDatabaseName("state_timestamp");

                    b.ToTable("state");
                });

            modelBuilder.Entity("CompatBot.Database.SyscallInfo", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasColumnName("id");

                    b.Property<string>("Function")
                        .IsRequired()
                        .HasColumnType("TEXT")
                        .HasColumnName("function");

                    b.HasKey("Id")
                        .HasName("id");

                    b.HasIndex("Function")
                        .HasDatabaseName("syscall_info_function");

                    b.ToTable("syscall_info");
                });

            modelBuilder.Entity("CompatBot.Database.SyscallToProductMap", b =>
                {
                    b.Property<int>("ProductId")
                        .HasColumnType("INTEGER")
                        .HasColumnName("product_id");

                    b.Property<int>("SyscallInfoId")
                        .HasColumnType("INTEGER")
                        .HasColumnName("syscall_info_id");

                    b.HasKey("ProductId", "SyscallInfoId")
                        .HasName("id");

                    b.HasIndex("SyscallInfoId")
                        .HasDatabaseName("ix_syscall_to_product_map_syscall_info_id");

                    b.ToTable("syscall_to_product_map");
                });

            modelBuilder.Entity("CompatBot.Database.Thumbnail", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasColumnName("id");

                    b.Property<long?>("CompatibilityChangeDate")
                        .HasColumnType("INTEGER")
                        .HasColumnName("compatibility_change_date");

                    b.Property<byte?>("CompatibilityStatus")
                        .HasColumnType("INTEGER")
                        .HasColumnName("compatibility_status");

                    b.Property<string>("ContentId")
                        .HasColumnType("TEXT")
                        .HasColumnName("content_id");

                    b.Property<int?>("EmbedColor")
                        .HasColumnType("INTEGER")
                        .HasColumnName("embed_color");

                    b.Property<string>("EmbeddableUrl")
                        .HasColumnType("TEXT")
                        .HasColumnName("embeddable_url");

                    b.Property<int?>("MetacriticId")
                        .HasColumnType("INTEGER")
                        .HasColumnName("metacritic_id");

                    b.Property<string>("Name")
                        .HasColumnType("TEXT")
                        .HasColumnName("name");

                    b.Property<string>("ProductCode")
                        .IsRequired()
                        .HasColumnType("TEXT")
                        .HasColumnName("product_code");

                    b.Property<long>("Timestamp")
                        .HasColumnType("INTEGER")
                        .HasColumnName("timestamp");

                    b.Property<string>("Url")
                        .HasColumnType("TEXT")
                        .HasColumnName("url");

                    b.HasKey("Id")
                        .HasName("id");

                    b.HasIndex("ContentId")
                        .IsUnique()
                        .HasDatabaseName("thumbnail_content_id");

                    b.HasIndex("MetacriticId")
                        .HasDatabaseName("ix_thumbnail_metacritic_id");

                    b.HasIndex("ProductCode")
                        .IsUnique()
                        .HasDatabaseName("thumbnail_product_code");

                    b.HasIndex("Timestamp")
                        .HasDatabaseName("thumbnail_timestamp");

                    b.ToTable("thumbnail");
                });

            modelBuilder.Entity("CompatBot.Database.SyscallToProductMap", b =>
                {
                    b.HasOne("CompatBot.Database.Thumbnail", "Product")
                        .WithMany("SyscallToProductMap")
                        .HasForeignKey("ProductId")
                        .HasConstraintName("fk_syscall_to_product_map__thumbnail_product_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("CompatBot.Database.SyscallInfo", "SyscallInfo")
                        .WithMany("SyscallToProductMap")
                        .HasForeignKey("SyscallInfoId")
                        .HasConstraintName("fk_syscall_to_product_map_syscall_info_syscall_info_id")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Product");

                    b.Navigation("SyscallInfo");
                });

            modelBuilder.Entity("CompatBot.Database.Thumbnail", b =>
                {
                    b.HasOne("CompatBot.Database.Metacritic", "Metacritic")
                        .WithMany()
                        .HasForeignKey("MetacriticId")
                        .HasConstraintName("fk_thumbnail_metacritic_metacritic_id");

                    b.Navigation("Metacritic");
                });

            modelBuilder.Entity("CompatBot.Database.SyscallInfo", b =>
                {
                    b.Navigation("SyscallToProductMap");
                });

            modelBuilder.Entity("CompatBot.Database.Thumbnail", b =>
                {
                    b.Navigation("SyscallToProductMap");
                });
#pragma warning restore 612, 618
        }
    }
}
