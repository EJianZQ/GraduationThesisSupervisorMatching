using GraduationThesisSupervisorMatching.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;


namespace GraduationThesisSupervisorMatching.Db
{
    public class SupervisorMatchingDbContext : DbContext
    {
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Student> Students { get; set; }
        public DbSet<Teacher> Teachers { get; set; }
        public DbSet<Grade> Grades { get; set; }
        public DbSet<Preference> Preferences { get; set; }
        public SupervisorMatchingDbContext(DbContextOptions options) : base(options)
        {

        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Admin>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Username)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.PasswordHash)
                    .IsRequired();
            });

            modelBuilder.Entity<Student>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.StudentID)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.PasswordHash)
                    .IsRequired();

                entity.Property(e => e.GPA)
                    .IsRequired()
                    .HasColumnType("decimal(4,3)");

                entity.HasOne(e => e.Grade)
                    .WithMany(g => g.Students)
                    .HasForeignKey(e => e.GradeId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.AssignedTeacher)
                    .WithMany(t => t.RegularStudents)
                    .HasForeignKey(e => e.AssignedTeacherId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(e => e.Preferences)
                    .WithOne(p => p.Student)
                    .HasForeignKey(p => p.StudentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Teacher>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.MaxCapacity)
                    .IsRequired();

                entity.Property(e => e.BestStudentId)
                    .IsConcurrencyToken();

                entity.HasOne(t => t.BestStudent)
                    .WithOne()
                    .HasForeignKey<Teacher>(t => t.BestStudentId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasMany(t => t.RegularStudents)
                    .WithOne(s => s.AssignedTeacher)
                    .HasForeignKey(s => s.AssignedTeacherId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasMany(e => e.Preferences)
                    .WithOne(p => p.Teacher)
                    .HasForeignKey(p => p.TeacherId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Grade)
                    .WithMany(g => g.Teachers)
                    .HasForeignKey(e => e.GradeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Grade>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.HasMany(e => e.Students)
                    .WithOne(s => s.Grade)
                    .HasForeignKey(s => s.GradeId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(g => g.Teachers)
                    .WithOne(t => t.Grade)
                    .HasForeignKey(t => t.GradeId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Preference>(entity =>
            {
                entity.HasKey(e => new { e.StudentId, e.TeacherId });

                entity.Property(e => e.PreferenceOrder)
                    .IsRequired();

                entity.HasOne(e => e.Student)
                    .WithMany(s => s.Preferences)
                    .HasForeignKey(e => e.StudentId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Teacher)
                    .WithMany(t => t.Preferences)
                    .HasForeignKey(e => e.TeacherId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
