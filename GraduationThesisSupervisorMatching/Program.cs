using GraduationThesisSupervisorMatching.Configs;
using GraduationThesisSupervisorMatching.Db;
using GraduationThesisSupervisorMatching.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAnyPolicy", builder =>
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader());
});

builder.WebHost.UseKestrel(options =>
{
    options.ListenAnyIP(5000); // 启用在所有网络接口上监听5000端口
});

#region 配置系统
builder.Services.Configure<DatabaseConfig>(builder.Configuration.GetSection("DatabaseConfig"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
#endregion

#region 数据库配置
builder.Services.AddDbContext<SupervisorMatchingDbContext>(options =>
{
    var databaseConfig = builder.Configuration.GetSection("DatabaseConfig").Get<DatabaseConfig>();
    options.UseMySql(databaseConfig.MySQLConnectionString, 
    ServerVersion.AutoDetect(databaseConfig.MySQLConnectionString));
});
#endregion

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})

.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,

        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["SecretKey"])),

        ClockSkew = TimeSpan.Zero // 默认的 5 分钟偏移，可以根据需要调整
    };
});

#region JWT配置

#endregion

#region 托管服务
builder.Services.AddHostedService<AdminInitializer>();
#endregion
var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.UseCors("AllowAnyPolicy");

app.UseAuthorization();

app.MapControllers();

app.Run();
