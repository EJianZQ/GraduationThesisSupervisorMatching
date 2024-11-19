
using GraduationThesisSupervisorMatching.Configs;
using GraduationThesisSupervisorMatching.Db;
using GraduationThesisSupervisorMatching.Models;
using Microsoft.AspNetCore.Identity;

namespace GraduationThesisSupervisorMatching.Services
{
    public class AdminInitializer : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public AdminInitializer(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }
        
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SupervisorMatchingDbContext>();

            var adminUsers = _configuration.GetSection("AdminUsers").Get<List<AdminUserConfig>>();

            if (adminUsers == null || !adminUsers.Any())
            {
                Console.WriteLine("未在配置中找到管理员用户列表。");
                return;
            }

            var passwordHasher = new PasswordHasher<Admin>();

            foreach (var adminUser in adminUsers)
            {
                if (string.IsNullOrEmpty(adminUser.Username))
                {
                    Console.WriteLine("管理员用户名不能为空。");
                    continue;
                }

                // 检查管理员用户是否已存在
                if (!context.Admins.Any(a => a.Username == adminUser.Username))
                {
                    // 尝试获取密码（从用户机密或环境变量）
                    if (string.IsNullOrEmpty(adminUser.Password))
                    {
                        // 构建密码的配置键
                        var passwordKey = $"AdminUsers:{adminUsers.IndexOf(adminUser)}:Password";
                        adminUser.Password = _configuration[passwordKey];
                    }

                    if (string.IsNullOrEmpty(adminUser.Password))
                    {
                        Console.WriteLine($"未找到管理员用户 {adminUser.Username} 的密码。请在用户机密或环境变量中设置。");
                        continue;
                    }

                    var admin = new Admin
                    {
                        Username = adminUser.Username
                    };

                    admin.PasswordHash = passwordHasher.HashPassword(admin, adminUser.Password);

                    context.Admins.Add(admin);
                    Console.WriteLine($"管理员用户已创建：{adminUser.Username}");
                }
                else
                {
                    Console.WriteLine($"管理员用户已存在：{adminUser.Username}");
                }

                await context.SaveChangesAsync();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}