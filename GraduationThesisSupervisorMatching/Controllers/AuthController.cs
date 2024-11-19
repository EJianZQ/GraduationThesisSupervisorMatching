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
                // ����Ա��¼�߼�
                var admin = await _dbContext.Admins.FirstOrDefaultAsync(a => a.Username == requestObj.Username);
                if (admin != null)
                {
                    var passwordHasher = new PasswordHasher<Admin>();
                    var result = passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, requestObj.Password);

                    if (result == PasswordVerificationResult.Success)
                    {
                        // ���� JWT ����
                        var token = GenerateJwtToken(admin.Username, admin.Username, "Admin");

                        response.Success = true;
                        response.Token = token;
                        response.Role = "Admin";
                        response.Message = "��¼�ɹ�";

                        return Ok(response);
                    }
                }

                // ��¼ʧ��
                response.Success = false;
                response.Message = "�û������������";
                return BadRequest(response);
            }
            else
            {
                // ѧ����¼�߼�
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
                        // ���� GPA �������޶���ͬһ�꼶��
                        var gpaRank = await _dbContext.Students
                            .Where(s => s.GradeId == student.GradeId && s.GPA > student.GPA)
                            .CountAsync() + 1;

                        // ���� JWT ����
                        var token = GenerateJwtToken(student.StudentID, student.Name, "Student");

                        response.Success = true;
                        response.Token = token;
                        response.Role = "Student";
                        response.Message = "��¼�ɹ�";
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
                    response.Message = "ѧ�Ż��������";
                    return BadRequest(response);
                }

                response.Success = false;
                response.Message = "ѧ�Ż��������";
                return BadRequest(response);
            }

        }

        [HttpPost("AddRangeStudents")]
        [Authorize(Roles ="Admin")]
        public async Task<ActionResult<StandardResponse>> AddRangeStudents([FromBody] AddStudentRangeRequest requestObj)
        {
            // ����������
            if (string.IsNullOrWhiteSpace(requestObj.Grade) || string.IsNullOrWhiteSpace(requestObj.StudentsInfoString))
            {
                return BadRequest("�꼶��ѧ����Ϣ����Ϊ�ա�");
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // ��ȡ�򴴽��꼶
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
                        return BadRequest(new StandardResponse() { success = false, message = $"�� {lineNumber} �и�ʽ����ȷ��'{line}'��ӦΪ 'StudentId|Password|StudentName|GPA' ��ʽ��" });
                    }

                    var studentId = fields[0].Trim();
                    var password = fields[1].Trim();
                    var studentName = fields[2].Trim();
                    var gpaString = fields[3].Trim();

                    // ��֤�ֶ��Ƿ�Ϊ��
                    if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(password) ||
                        string.IsNullOrWhiteSpace(studentName) || string.IsNullOrWhiteSpace(gpaString))
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new StandardResponse() { success = false, message = $"�� {lineNumber} �а������ֶΣ�'{line}'��" });

                    }

                    // ��֤ GPA �Ƿ�Ϊ��Ч������
                    if (!decimal.TryParse(gpaString, out decimal gpa))
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new StandardResponse() { success = false, message = $"�� {lineNumber} �� GPA ��Ч��'{gpaString}'��" });

                    }

                    // ��� GPA ��Χ��0.00 �� 5.00��
                    if (gpa < 0.00m || gpa > 5.00m)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new StandardResponse() { success = false, message = $"�� {lineNumber} �� GPA ������Ч��Χ��0.00 - 5.00����'{gpa}'��" });

                    }

                    // ���ѧ���Ƿ��Ѵ���
                    var existingStudent = await _dbContext.Students.AnyAsync(s => s.StudentID == studentId);
                    if (existingStudent)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new StandardResponse() { success = false, message = $"�� {lineNumber} �У�ѧ���Ѵ��ڣ�ѧ�� '{studentId}'��" });

                    }

                    // ����ѧ������
                    var student = new Student
                    {
                        StudentID = studentId,
                        Name = studentName,
                        GPA = gpa,
                        GradeId = grade.Id
                    };

                    // ���������ϣ
                    student.PasswordHash = passwordHasher.HashPassword(student, password);

                    studentsToAdd.Add(student);
                }

                // ��ѧ����ӵ����ݿ�
                _dbContext.Students.AddRange(studentsToAdd);
                await _dbContext.SaveChangesAsync();

                // �ύ����
                await transaction.CommitAsync();

                return Ok(new StandardResponse() { success = true, message = $"�ɹ������ {studentsToAdd.Count} ��ѧ����" });
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

            // ��� studentId �Ƿ�Ϊ��
            if (string.IsNullOrWhiteSpace(studentId))
            {
                response.success = false;
                response.message = "ѧ��ѧ�Ų���Ϊ�ա�";
                return BadRequest(response);
            }

            // �����ݿ��в���ѧ��
            var student = await _dbContext.Students
                .Include(s => s.Preferences)
                .FirstOrDefaultAsync(s => s.StudentID == studentId);

            if (student == null)
            {
                response.success = false;
                response.message = $"δ�ҵ�ѧ��Ϊ '{studentId}' ��ѧ����";
                return NotFound(response);
            }

            // ��ʼ���ݿ�����
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // ɾ����ѧ����ص�ƫ�ü�¼
                if (student.Preferences != null && student.Preferences.Any())
                {
                    _dbContext.Preferences.RemoveRange(student.Preferences);
                }

                // ɾ��ѧ����¼
                _dbContext.Students.Remove(student);

                // �������
                await _dbContext.SaveChangesAsync();

                // �ύ����
                await transaction.CommitAsync();

                response.success = true;
                response.message = $"�ɹ�ɾ��ѧ��Ϊ '{studentId}' ��ѧ����";
                return Ok(response);
            }
            catch (Exception ex)
            {
                // �ع�����
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"ɾ��ѧ��ʱ��������{ex.Message}";
                return StatusCode(500, response);
            }
        }

        [HttpPost("ChangePassword")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> ChangePassword([FromBody] ChangePasswordRequest requestObj)
        {
            var response = new StandardResponse();

            // �����������Ƿ�Ϊ��
            if (string.IsNullOrWhiteSpace(requestObj.StudentID))
            {
                response.success = false;
                response.message = "ѧ��ѧ�Ų���Ϊ�ա�";
                return BadRequest(response);
            }

            if (string.IsNullOrWhiteSpace(requestObj.NewPassword))
            {
                response.success = false;
                response.message = "�����벻��Ϊ�ա�";
                return BadRequest(response);
            }

            // �����ݿ��в���ѧ��
            var student = await _dbContext.Students.FirstOrDefaultAsync(s => s.StudentID == requestObj.StudentID);

            if (student == null)
            {
                response.success = false;
                response.message = $"δ�ҵ�ѧ��Ϊ '{requestObj.StudentID}' ��ѧ����";
                return NotFound(response);
            }

            try
            {
                // ���������ϣ��
                var passwordHasher = new PasswordHasher<Student>();

                // ����������й�ϣ����
                student.PasswordHash = passwordHasher.HashPassword(student, requestObj.NewPassword);

                // �������
                await _dbContext.SaveChangesAsync();

                response.success = true;
                response.message = $"�ɹ��޸�ѧ��Ϊ '{requestObj.StudentID}' ��ѧ�����롣";
                return Ok(response);
            }
            catch (Exception ex)
            {
                response.success = false;
                response.message = $"�޸�����ʱ��������{ex.Message}";
                return StatusCode(500, response);
            }
        }

        [HttpPost("AddTeacher")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> AddTeacher([FromBody] AddTeacherRequest requestObj)
        {
            var response = new StandardResponse();

            // ������֤
            if (string.IsNullOrWhiteSpace(requestObj.Name))
            {
                response.success = false;
                response.message = "��ʦ��������Ϊ�ա�";
                return BadRequest(response);
            }

            if (requestObj.MaxCapacity <= 0)
            {
                response.success = false;
                response.message = "��ʦ�������������Ϊ��������";
                return BadRequest(response);
            }

            if (requestObj.GradeId <= 0)
            {
                response.success = false;
                response.message = "�꼶 ID ��Ч��";
                return BadRequest(response);
            }

            if (string.IsNullOrWhiteSpace(requestObj.Description))
            {
                response.success = false;
                response.message = "��ʦ��������Ϊ�ա�";
                return BadRequest(response);
            }

            // ��֤������������Ƿ�Ϊ�Ǹ�����
            if (requestObj.UpperLevelCapacity < 0)
            {
                response.success = false;
                response.message = "�ϲ�ѧ������������Ϊ������";
                return BadRequest(response);
            }

            if (requestObj.MiddleLevelCapacity < 0)
            {
                response.success = false;
                response.message = "�в�ѧ������������Ϊ������";
                return BadRequest(response);
            }

            if (requestObj.LowerLevelCapacity < 0)
            {
                response.success = false;
                response.message = "�²�ѧ������������Ϊ������";
                return BadRequest(response);
            }

            // ����������
            int totalCapacity = requestObj.UpperLevelCapacity + requestObj.MiddleLevelCapacity + requestObj.LowerLevelCapacity;

            if (requestObj.AcceptsTopStudent)
            {
                totalCapacity += 1; // ������ܶ���ѧ������������1
            }

            if (totalCapacity > requestObj.MaxCapacity)
            {
                response.success = false;
                response.message = "�����ѧ��������֮�ͣ���������ѧ�������ܳ�����ʦ�����������";
                return BadRequest(response);
            }

            // ���ָ�����꼶�Ƿ����
            var grade = await _dbContext.Grades.FindAsync(requestObj.GradeId);
            if (grade == null)
            {
                response.success = false;
                response.message = $"ָ�����꼶��ID: {requestObj.GradeId}�������ڡ�";
                return NotFound(response);
            }

            // ����Ƿ��Ѵ���ͬ���Ľ�ʦ
            var existingTeacher = await _dbContext.Teachers.FirstOrDefaultAsync(t => t.Name == requestObj.Name && t.GradeId == requestObj.GradeId);
            if (existingTeacher != null)
            {
                response.success = false;
                response.message = $"��ʦ '{requestObj.Name}' �Ѿ������ڸ��꼶��";
                return Conflict(response); // ���� 409 Conflict ״̬��
            }

            try
            {
                // �����µĽ�ʦ����
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
                    // BestStudentId �� BestStudent �ڴ���ʱΪ��
                };

                // ��ӵ����ݿ�������
                _dbContext.Teachers.Add(teacher);

                // ������ĵ����ݿ�
                await _dbContext.SaveChangesAsync();

                // �����ɹ���Ӧ
                response.success = true;
                response.message = $"�ɹ���ӽ�ʦ '{teacher.Name}'��";
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
                // �����쳣
                response.success = false;
                response.message = $"��ӽ�ʦʱ��������{ex.Message}";
                return StatusCode(500, response); // ���� 500 Internal Server Error
            }
        }


        [HttpPost("DeleteTeacher")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> DeleteTeacher([FromQuery] long teacherId)
        {
            var response = new StandardResponse();

            // ��֤ teacherId �Ƿ���Ч
            if (teacherId <= 0)
            {
                response.success = false;
                response.message = "��ʦ ID ��Ч��";
                return BadRequest(response);
            }

            // �����ݿ��в��ҽ�ʦ������������ѧ����ƫ��
            var teacher = await _dbContext.Teachers
                .Include(t => t.RegularStudents)
                .Include(t => t.Preferences)
                .FirstOrDefaultAsync(t => t.Id == teacherId);

            if (teacher == null)
            {
                response.success = false;
                response.message = $"δ�ҵ� ID Ϊ {teacherId} �Ľ�ʦ��";
                return NotFound(response);
            }

            // ����ʦ�Ƿ��з����ѧ��
            if (teacher.RegularStudents != null && teacher.RegularStudents.Any())
            {
                response.success = false;
                response.message = $"��ʦ '{teacher.Name}' ����ѧ�����䣬�޷�ɾ����";
                return BadRequest(response);
            }

            // ��ʼ���ݿ�����
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // ɾ�����ʦ��ص�ƫ�ü�¼
                if (teacher.Preferences != null && teacher.Preferences.Any())
                {
                    _dbContext.Preferences.RemoveRange(teacher.Preferences);
                }

                // ɾ����ʦ��¼
                _dbContext.Teachers.Remove(teacher);

                // �������
                await _dbContext.SaveChangesAsync();

                // �ύ����
                await transaction.CommitAsync();

                response.success = true;
                response.message = $"�ɹ�ɾ����ʦ '{teacher.Name}'��";
                return Ok(response);
            }
            catch (Exception ex)
            {
                // �ع�����
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"ɾ����ʦʱ��������{ex.Message}";
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
                response.message = "�ɹ���ȡ�����꼶��";
                response.Data = grades;

                return Ok(response);
            }
            catch (Exception ex)
            {
                response.success = false;
                response.message = $"��ȡ�꼶ʱ��������{ex.Message}";
                return StatusCode(500, response);
            }
        }

        /// <summary>
        /// ͨ���꼶��ȡѧ����Ϣ
        /// </summary>
        /// <param name="gradeId"></param>
        /// <returns></returns>
        [HttpGet("GetStudents")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> GetStudentsByGrade([FromQuery] long gradeId)
        {
            var response = new StandardResponse();

            // ��֤ gradeId �Ƿ���Ч
            if (gradeId <= 0)
            {
                response.success = false;
                response.message = "�꼶 ID ��Ч��";
                return BadRequest(response);
            }

            // ��ȡָ�����꼶
            var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Id == gradeId);
            if (grade == null)
            {
                response.success = false;
                response.message = $"δ�ҵ� ID Ϊ {gradeId} ���꼶��";
                return NotFound(response);
            }

            try
            {
                // ��ȡ���꼶������ѧ����������صĵ�������
                var students = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId)
                    .Include(s => s.AssignedTeacher)
                    .Include(s => s.Preferences.OrderBy(p => p.PreferenceOrder))
                        .ThenInclude(p => p.Teacher)
                    .ToListAsync();

                // ��ȡ�꼶�����н�ʦ
                var teachersInGrade = await _dbContext.Teachers
                    .Where(t => t.GradeId == gradeId)
                    .ToListAsync();

                // ���㶥��ѧ������
                int topStudentCount = teachersInGrade.Count(t => t.AcceptsTopStudent);

                // ������������
                int totalUpperCapacity = teachersInGrade.Sum(t => t.UpperLevelCapacity);
                int totalMiddleCapacity = teachersInGrade.Sum(t => t.MiddleLevelCapacity);
                int totalLowerCapacity = teachersInGrade.Sum(t => t.LowerLevelCapacity);

                // �� GPA ��������ѧ���б�
                var studentsOrderedByGPA = students.OrderByDescending(s => s.GPA).ToList();

                // ���� GPA ������������ͬ GPA �����
                var gpaGroups = studentsOrderedByGPA
                    .GroupBy(s => s.GPA)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                var overallRank = 1;
                var studentsWithLevelAndRank = new List<(Student Student, int GPARank, string Level, int LevelRank)>();

                // ��ʼ�����ʣ������
                int remainingTopCapacity = topStudentCount;
                int remainingUpperCapacity = totalUpperCapacity;
                int remainingMiddleCapacity = totalMiddleCapacity;
                int remainingLowerCapacity = totalLowerCapacity;

                // Ϊÿ����γ�ʼ�� levelRank ������
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

                    // �жϸ� GPA ���ѧ��Ӧ�÷��䵽�ĸ����
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
                        // û���㹻�����������Ϊ "Unassigned"
                        level = "Unassigned";
                    }

                    // ��ȡ��ǰ��ε� levelRank ������
                    int levelRankCounter = levelRankCounters[level];

                    foreach (var student in studentsInGroup)
                    {
                        // ��ʽָ��Ԫ��Ԫ�ص�����
                        studentsWithLevelAndRank.Add((Student: student, GPARank: overallRank, Level: level, LevelRank: levelRankCounter));
                        levelRankCounter++;
                    }

                    // ���¸ò�ε� levelRank ������
                    levelRankCounters[level] = levelRankCounter;

                    overallRank += groupCount;
                }

                // ���� StudentDetailDTO �б�
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
                response.message = "��ȡѧ���б�ɹ���";
                response.Data = studentDetailDTOs;

                return Ok(response);
            }
            catch (Exception ex)
            {
                response.success = false;
                response.message = $"��ȡѧ���б�ʱ��������{ex.Message}";
                return StatusCode(500, response);
            }
        }




        /// <summary>
        /// JWT �������ɷ���
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
        new Claim(ClaimTypes.NameIdentifier, userId), // ��� NameIdentifier ����
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


