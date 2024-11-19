using GraduationThesisSupervisorMatching.Db;
using GraduationThesisSupervisorMatching.DTO;
using GraduationThesisSupervisorMatching.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace GraduationThesisSupervisorMatching.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly SupervisorMatchingDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public AuthController(SupervisorMatchingDbContext dbcontext, IConfiguration configuration)
        {
            _dbContext = dbcontext;
            _configuration = configuration;
        }


        [HttpPost("Login")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest requestObj)
        {
            var response = new LoginResponse();

            if (requestObj.Type == "Admin")
            {
                // 管理员登录逻辑
                var admin = await _dbContext.Admins.FirstOrDefaultAsync(a => a.Username == requestObj.Username);
                if (admin != null)
                {
                    var passwordHasher = new PasswordHasher<Admin>();
                    var result = passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, requestObj.Password);

                    if (result == PasswordVerificationResult.Success)
                    {
                        // 生成 JWT 令牌
                        var token = GenerateJwtToken(admin.Username, admin.Username, "Admin");

                        response.Success = true;
                        response.Token = token;
                        response.Role = "Admin";
                        response.Message = "登录成功";

                        return Ok(response);
                    }
                }

                // 登录失败
                response.Success = false;
                response.Message = "用户名或密码错误";
                return BadRequest(response);
            }
            else
            {
                // 学生登录逻辑
                var student = await _dbContext.Students
                .Include(s => s.Grade)
                .Include(s => s.Preferences.OrderBy(p => p.PreferenceOrder))
                    .ThenInclude(p => p.Teacher)
                .Include(s => s.AssignedTeacher)
                .FirstOrDefaultAsync(s => s.StudentID == requestObj.Username);

                if (student != null)
                {
                    var passwordHasher = new PasswordHasher<Student>();
                    var result = passwordHasher.VerifyHashedPassword(student, student.PasswordHash, requestObj.Password);

                    if (result == PasswordVerificationResult.Success)
                    {
                        // 计算 GPA 排名（限定在同一年级）
                        var gpaRank = await _dbContext.Students
                            .Where(s => s.GradeId == student.GradeId && s.GPA > student.GPA)
                            .CountAsync() + 1;

                        // 生成 JWT 令牌
                        var token = GenerateJwtToken(student.StudentID, student.Name, "Student");

                        response.Success = true;
                        response.Token = token;
                        response.Role = "Student";
                        response.Message = "登录成功";
                        response.Name = student.Name;
                        response.StudentId = student.StudentID;
                        response.GPA = student.GPA.ToString("0.00");
                        response.GPARank = gpaRank;
                        response.GradeName = student.Grade.Name;
                        response.GradeId = student.Grade.Id;
                        response.PreferTeachersName = student.Preferences
                            .OrderBy(p => p.PreferenceOrder)
                            .Select(p => p.Teacher.Name)
                            .ToList();
                        response.AssignTeacherName = student.AssignedTeacher?.Name;

                        return Ok(response);
                    }

                    response.Success = false;
                    response.Message = "学号或密码错误";
                    return BadRequest(response);
                }

                response.Success = false;
                response.Message = "学号或密码错误";
                return BadRequest(response);
            }

        }

        [HttpPost("AddRangeStudents")]
        [Authorize(Roles ="Admin")]
        public async Task<ActionResult<StandardResponse>> AddRangeStudents([FromBody] AddStudentRangeRequest requestObj)
        {
            // 检查输入参数
            if (string.IsNullOrWhiteSpace(requestObj.Grade) || string.IsNullOrWhiteSpace(requestObj.StudentsInfoString))
            {
                return BadRequest("年级和学生信息不能为空。");
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 获取或创建年级
                var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Name == requestObj.Grade);
                if (grade == null)
                {
                    grade = new Grade
                    {
                        Name = requestObj.Grade
                    };
                    _dbContext.Grades.Add(grade);
                    await _dbContext.SaveChangesAsync();
                }

                var passwordHasher = new PasswordHasher<Student>();

                var studentsInfoLines = requestObj.StudentsInfoString.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var studentsToAdd = new List<Student>();
                var lineNumber = 0;

                foreach(var line in studentsInfoLines)
                {
                    lineNumber++;
                    var fields = line.Split('|');
                    if (fields.Length != 4)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new StandardResponse() { success = false, message = $"第 {lineNumber} 行格式不正确：'{line}'。应为 'StudentId|Password|StudentName|GPA' 格式。" });
                    }

                    var studentId = fields[0].Trim();
                    var password = fields[1].Trim();
                    var studentName = fields[2].Trim();
                    var gpaString = fields[3].Trim();

                    // 验证字段是否为空
                    if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(password) ||
                        string.IsNullOrWhiteSpace(studentName) || string.IsNullOrWhiteSpace(gpaString))
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new StandardResponse() { success = false, message = $"第 {lineNumber} 行包含空字段：'{line}'。" });

                    }

                    // 验证 GPA 是否为有效的数字
                    if (!decimal.TryParse(gpaString, out decimal gpa))
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new StandardResponse() { success = false, message = $"第 {lineNumber} 行 GPA 无效：'{gpaString}'。" });

                    }

                    // 检查 GPA 范围（0.00 到 5.00）
                    if (gpa < 0.00m || gpa > 5.00m)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new StandardResponse() { success = false, message = $"第 {lineNumber} 行 GPA 超出有效范围（0.00 - 5.00）：'{gpa}'。" });

                    }

                    // 检查学生是否已存在
                    var existingStudent = await _dbContext.Students.AnyAsync(s => s.StudentID == studentId);
                    if (existingStudent)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new StandardResponse() { success = false, message = $"第 {lineNumber} 行：学生已存在，学号 '{studentId}'。" });

                    }

                    // 创建学生对象
                    var student = new Student
                    {
                        StudentID = studentId,
                        Name = studentName,
                        GPA = gpa,
                        GradeId = grade.Id
                    };

                    // 生成密码哈希
                    student.PasswordHash = passwordHasher.HashPassword(student, password);

                    studentsToAdd.Add(student);
                }

                // 将学生添加到数据库
                _dbContext.Students.AddRange(studentsToAdd);
                await _dbContext.SaveChangesAsync();

                // 提交事务
                await transaction.CommitAsync();

                return Ok(new StandardResponse() { success = true, message = $"成功添加了 {studentsToAdd.Count} 名学生。" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(new StandardResponse() { success = true, message = ex.Message});
            }
        }

        [HttpPost("DeleteStudent")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> DeleteStudent([FromQuery] string studentId)
        {
            var response = new StandardResponse();

            // 检查 studentId 是否为空
            if (string.IsNullOrWhiteSpace(studentId))
            {
                response.success = false;
                response.message = "学生学号不能为空。";
                return BadRequest(response);
            }

            // 在数据库中查找学生
            var student = await _dbContext.Students
                .Include(s => s.Preferences)
                .FirstOrDefaultAsync(s => s.StudentID == studentId);

            if (student == null)
            {
                response.success = false;
                response.message = $"未找到学号为 '{studentId}' 的学生。";
                return NotFound(response);
            }

            // 开始数据库事务
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 删除与学生相关的偏好记录
                if (student.Preferences != null && student.Preferences.Any())
                {
                    _dbContext.Preferences.RemoveRange(student.Preferences);
                }

                // 删除学生记录
                _dbContext.Students.Remove(student);

                // 保存更改
                await _dbContext.SaveChangesAsync();

                // 提交事务
                await transaction.CommitAsync();

                response.success = true;
                response.message = $"成功删除学号为 '{studentId}' 的学生。";
                return Ok(response);
            }
            catch (Exception ex)
            {
                // 回滚事务
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"删除学生时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }

        [HttpPost("ChangePassword")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> ChangePassword([FromBody] ChangePasswordRequest requestObj)
        {
            var response = new StandardResponse();

            // 检查输入参数是否为空
            if (string.IsNullOrWhiteSpace(requestObj.StudentID))
            {
                response.success = false;
                response.message = "学生学号不能为空。";
                return BadRequest(response);
            }

            if (string.IsNullOrWhiteSpace(requestObj.NewPassword))
            {
                response.success = false;
                response.message = "新密码不能为空。";
                return BadRequest(response);
            }

            // 在数据库中查找学生
            var student = await _dbContext.Students.FirstOrDefaultAsync(s => s.StudentID == requestObj.StudentID);

            if (student == null)
            {
                response.success = false;
                response.message = $"未找到学号为 '{requestObj.StudentID}' 的学生。";
                return NotFound(response);
            }

            try
            {
                // 创建密码哈希器
                var passwordHasher = new PasswordHasher<Student>();

                // 对新密码进行哈希处理
                student.PasswordHash = passwordHasher.HashPassword(student, requestObj.NewPassword);

                // 保存更改
                await _dbContext.SaveChangesAsync();

                response.success = true;
                response.message = $"成功修改学号为 '{requestObj.StudentID}' 的学生密码。";
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.success = false;
                response.message = $"修改密码时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }

        [HttpPost("AddTeacher")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> AddTeacher([FromBody] AddTeacherRequest requestObj)
        {
            var response = new StandardResponse();

            // 输入验证
            if (string.IsNullOrWhiteSpace(requestObj.Name))
            {
                response.success = false;
                response.message = "教师姓名不能为空。";
                return BadRequest(response);
            }

            if (requestObj.MaxCapacity <= 0)
            {
                response.success = false;
                response.message = "教师的最大容量必须为正整数。";
                return BadRequest(response);
            }

            if (requestObj.GradeId <= 0)
            {
                response.success = false;
                response.message = "年级 ID 无效。";
                return BadRequest(response);
            }

            if (string.IsNullOrWhiteSpace(requestObj.Description))
            {
                response.success = false;
                response.message = "教师描述不能为空。";
                return BadRequest(response);
            }

            // 验证各层次容纳数是否为非负整数
            if (requestObj.UpperLevelCapacity < 0)
            {
                response.success = false;
                response.message = "上层学生容纳数不能为负数。";
                return BadRequest(response);
            }

            if (requestObj.MiddleLevelCapacity < 0)
            {
                response.success = false;
                response.message = "中层学生容纳数不能为负数。";
                return BadRequest(response);
            }

            if (requestObj.LowerLevelCapacity < 0)
            {
                response.success = false;
                response.message = "下层学生容纳数不能为负数。";
                return BadRequest(response);
            }

            // 计算总容量
            int totalCapacity = requestObj.UpperLevelCapacity + requestObj.MiddleLevelCapacity + requestObj.LowerLevelCapacity;

            if (requestObj.AcceptsTopStudent)
            {
                totalCapacity += 1; // 如果接受顶尖学生，总容量加1
            }

            if (totalCapacity > requestObj.MaxCapacity)
            {
                response.success = false;
                response.message = "各层次学生容纳数之和（包含顶尖学生）不能超过教师的最大容量。";
                return BadRequest(response);
            }

            // 检查指定的年级是否存在
            var grade = await _dbContext.Grades.FindAsync(requestObj.GradeId);
            if (grade == null)
            {
                response.success = false;
                response.message = $"指定的年级（ID: {requestObj.GradeId}）不存在。";
                return NotFound(response);
            }

            // 检查是否已存在同名的教师
            var existingTeacher = await _dbContext.Teachers.FirstOrDefaultAsync(t => t.Name == requestObj.Name && t.GradeId == requestObj.GradeId);
            if (existingTeacher != null)
            {
                response.success = false;
                response.message = $"教师 '{requestObj.Name}' 已经存在于该年级。";
                return Conflict(response); // 返回 409 Conflict 状态码
            }

            try
            {
                // 创建新的教师对象
                var teacher = new Teacher
                {
                    Name = requestObj.Name,
                    MaxCapacity = requestObj.MaxCapacity,
                    GradeId = requestObj.GradeId,
                    Description = requestObj.Description,
                    AcceptsTopStudent = requestObj.AcceptsTopStudent,
                    UpperLevelCapacity = requestObj.UpperLevelCapacity,
                    MiddleLevelCapacity = requestObj.MiddleLevelCapacity,
                    LowerLevelCapacity = requestObj.LowerLevelCapacity,
                    // BestStudentId 和 BestStudent 在创建时为空
                };

                // 添加到数据库上下文
                _dbContext.Teachers.Add(teacher);

                // 保存更改到数据库
                await _dbContext.SaveChangesAsync();

                // 构建成功响应
                response.success = true;
                response.message = $"成功添加教师 '{teacher.Name}'。";
                response.Data = new
                {
                    teacher.Id,
                    teacher.Name,
                    teacher.MaxCapacity,
                    teacher.CurrentCapacity,
                    GradeId = teacher.GradeId,
                    GradeName = grade.Name,
                    teacher.Description,
                    teacher.AcceptsTopStudent,
                    teacher.UpperLevelCapacity,
                    teacher.MiddleLevelCapacity,
                    teacher.LowerLevelCapacity
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                // 处理异常
                response.success = false;
                response.message = $"添加教师时发生错误：{ex.Message}";
                return StatusCode(500, response); // 返回 500 Internal Server Error
            }
        }


        [HttpPost("DeleteTeacher")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> DeleteTeacher([FromQuery] long teacherId)
        {
            var response = new StandardResponse();

            // 验证 teacherId 是否有效
            if (teacherId <= 0)
            {
                response.success = false;
                response.message = "教师 ID 无效。";
                return BadRequest(response);
            }

            // 在数据库中查找教师，包含关联的学生和偏好
            var teacher = await _dbContext.Teachers
                .Include(t => t.RegularStudents)
                .Include(t => t.Preferences)
                .FirstOrDefaultAsync(t => t.Id == teacherId);

            if (teacher == null)
            {
                response.success = false;
                response.message = $"未找到 ID 为 {teacherId} 的教师。";
                return NotFound(response);
            }

            // 检查教师是否有分配的学生
            if (teacher.RegularStudents != null && teacher.RegularStudents.Any())
            {
                response.success = false;
                response.message = $"教师 '{teacher.Name}' 仍有学生分配，无法删除。";
                return BadRequest(response);
            }

            // 开始数据库事务
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // 删除与教师相关的偏好记录
                if (teacher.Preferences != null && teacher.Preferences.Any())
                {
                    _dbContext.Preferences.RemoveRange(teacher.Preferences);
                }

                // 删除教师记录
                _dbContext.Teachers.Remove(teacher);

                // 保存更改
                await _dbContext.SaveChangesAsync();

                // 提交事务
                await transaction.CommitAsync();

                response.success = true;
                response.message = $"成功删除教师 '{teacher.Name}'。";
                return Ok(response);
            }
            catch (Exception ex)
            {
                // 回滚事务
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"删除教师时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }

        [HttpGet("GetGrade")]
        [Authorize]
        public async Task<ActionResult<StandardResponse>> GetAllGrades()
        {
            var response = new StandardResponse();

            try
            {
                var grades = await _dbContext.Grades
                    .Select(g => new GradeDTO
                    {
                        Id = g.Id,
                        Name = g.Name
                    })
                    .ToListAsync();

                response.success = true;
                response.message = "成功获取所有年级。";
                response.Data = grades;

                return Ok(response);
            }
            catch (Exception ex)
            {
                response.success = false;
                response.message = $"获取年级时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }

        /// <summary>
        /// 通过年级获取学生信息
        /// </summary>
        /// <param name="gradeId"></param>
        /// <returns></returns>
        [HttpGet("GetStudents")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> GetStudentsByGrade([FromQuery] long gradeId)
        {
            var response = new StandardResponse();

            // 验证 gradeId 是否有效
            if (gradeId <= 0)
            {
                response.success = false;
                response.message = "年级 ID 无效。";
                return BadRequest(response);
            }

            // 获取指定的年级
            var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Id == gradeId);
            if (grade == null)
            {
                response.success = false;
                response.message = $"未找到 ID 为 {gradeId} 的年级。";
                return NotFound(response);
            }

            try
            {
                // 获取该年级的所有学生，包含相关的导航属性
                var students = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId)
                    .Include(s => s.AssignedTeacher)
                    .Include(s => s.Preferences.OrderBy(p => p.PreferenceOrder))
                        .ThenInclude(p => p.Teacher)
                    .ToListAsync();

                // 获取年级内所有教师
                var teachersInGrade = await _dbContext.Teachers
                    .Where(t => t.GradeId == gradeId)
                    .ToListAsync();

                // 计算顶尖学生数量
                int topStudentCount = teachersInGrade.Count(t => t.AcceptsTopStudent);

                // 计算各层次容量
                int totalUpperCapacity = teachersInGrade.Sum(t => t.UpperLevelCapacity);
                int totalMiddleCapacity = teachersInGrade.Sum(t => t.MiddleLevelCapacity);
                int totalLowerCapacity = teachersInGrade.Sum(t => t.LowerLevelCapacity);

                // 按 GPA 降序排序学生列表
                var studentsOrderedByGPA = students.OrderByDescending(s => s.GPA).ToList();

                // 计算 GPA 排名，处理相同 GPA 的情况
                var gpaGroups = studentsOrderedByGPA
                    .GroupBy(s => s.GPA)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                var overallRank = 1;
                var studentsWithLevelAndRank = new List<(Student Student, int GPARank, string Level, int LevelRank)>();

                // 初始化层次剩余容量
                int remainingTopCapacity = topStudentCount;
                int remainingUpperCapacity = totalUpperCapacity;
                int remainingMiddleCapacity = totalMiddleCapacity;
                int remainingLowerCapacity = totalLowerCapacity;

                // 为每个层次初始化 levelRank 计数器
                var levelRankCounters = new Dictionary<string, int>
        {
            { "Top", 1 },
            { "Upper", 1 },
            { "Middle", 1 },
            { "Lower", 1 },
            { "Unassigned", 1 }
        };

                foreach (var group in gpaGroups)
                {
                    var gpaValue = group.Key;
                    var studentsInGroup = group.ToList();
                    int groupCount = studentsInGroup.Count;

                    string level = null;

                    // 判断该 GPA 组的学生应该分配到哪个层次
                    if (remainingTopCapacity >= groupCount)
                    {
                        level = "Top";
                        remainingTopCapacity -= groupCount;
                    }
                    else if (remainingUpperCapacity >= groupCount)
                    {
                        level = "Upper";
                        remainingUpperCapacity -= groupCount;
                    }
                    else if (remainingMiddleCapacity >= groupCount)
                    {
                        level = "Middle";
                        remainingMiddleCapacity -= groupCount;
                    }
                    else if (remainingLowerCapacity >= groupCount)
                    {
                        level = "Lower";
                        remainingLowerCapacity -= groupCount;
                    }
                    else
                    {
                        // 没有足够的容量，标记为 "Unassigned"
                        level = "Unassigned";
                    }

                    // 获取当前层次的 levelRank 计数器
                    int levelRankCounter = levelRankCounters[level];

                    foreach (var student in studentsInGroup)
                    {
                        // 显式指定元组元素的名称
                        studentsWithLevelAndRank.Add((Student: student, GPARank: overallRank, Level: level, LevelRank: levelRankCounter));
                        levelRankCounter++;
                    }

                    // 更新该层次的 levelRank 计数器
                    levelRankCounters[level] = levelRankCounter;

                    overallRank += groupCount;
                }

                // 构建 StudentDetailDTO 列表
                var studentDetailDTOs = studentsWithLevelAndRank.Select(sr => new StudentDetailDTO
                {
                    Id = sr.Student.Id,
                    StudentID = sr.Student.StudentID,
                    Name = sr.Student.Name,
                    GPA = sr.Student.GPA,
                    GPARank = sr.GPARank,
                    Level = sr.Level,
                    LevelRank = sr.LevelRank,
                    AssignedTeacher = sr.Student.AssignedTeacher != null
                        ? new AssignedTeacherDTO
                        {
                            Id = sr.Student.AssignedTeacher.Id,
                            Name = sr.Student.AssignedTeacher.Name
                        }
                        : null,
                    PreferenceTeacherNames = sr.Student.Preferences
                        .Select(p => p.Teacher.Name)
                        .ToList(),
                    GradeId = grade.Id,
                    GradeName = grade.Name
                }).ToList();

                response.success = true;
                response.message = "获取学生列表成功。";
                response.Data = studentDetailDTOs;

                return Ok(response);
            }
            catch (Exception ex)
            {
                response.success = false;
                response.message = $"获取学生列表时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }




        /// <summary>
        /// JWT 令牌生成方法
        /// </summary>
        /// <param name="username"></param>
        /// <param name="role"></param>
        /// <returns></returns>
        private string GenerateJwtToken(string userId, string username, string role)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"];
            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];
            var expires = double.Parse(jwtSettings["Expires"]);

            var claims = new[]
            {
        new Claim(ClaimTypes.NameIdentifier, userId), // 添加 NameIdentifier 声明
        new Claim(ClaimTypes.Name, username),
        new Claim(ClaimTypes.Role, role)
    };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.Now.AddHours(expires),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}


