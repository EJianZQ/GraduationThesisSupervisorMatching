using ClosedXML.Excel;
using GraduationThesisSupervisorMatching.Db;
using GraduationThesisSupervisorMatching.DTO;
using GraduationThesisSupervisorMatching.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace GraduationThesisSupervisorMatching.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BusinessController : ControllerBase
    {
        private readonly SupervisorMatchingDbContext _dbContext;
        private readonly IConfiguration _configuration;
        public BusinessController(SupervisorMatchingDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
        }

        /// <summary>
        /// 获取指定年级的所有教师
        /// </summary>
        /// <param name="gradeId"></param>
        /// <returns></returns>
        [HttpGet("GetTeachers")]
        [Authorize]
        public async Task<ActionResult<StandardResponse>> GetTeachersByGrade([FromQuery] long gradeId)
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
                // 获取当前用户的角色
                var userRoles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

                // 如果用户是 Admin
                if (userRoles.Contains("Admin"))
                {
                    // 获取该年级的所有教师
                    var teachers = await _dbContext.Teachers
                        .Where(t => t.GradeId == gradeId)
                        .Include(t => t.RegularStudents)
                        .Include(t => t.BestStudent)
                        .ToListAsync();

                    // 获取该年级所有学生的偏好
                    var preferences = await _dbContext.Preferences
                        .Where(p => p.Student.GradeId == gradeId)
                        .ToListAsync();

                    // 按教师ID分组，计算每个教师被选择的次数
                    var preferenceCounts = preferences
                        .GroupBy(p => p.TeacherId)
                        .Select(g => new { TeacherId = g.Key, Count = g.Count() })
                        .ToDictionary(k => k.TeacherId, v => v.Count);

                    // 构建教师列表 DTO
                    var teacherDtos = teachers.Select(teacher => new TeacherDTO
                    {
                        Id = teacher.Id,
                        Name = teacher.Name,
                        GradeName = grade.Name,
                        LevelCapacities = new LevelCapacitiesDTO
                        {
                            UpperLevelCapacity = teacher.UpperLevelCapacity,
                            MiddleLevelCapacity = teacher.MiddleLevelCapacity,
                            LowerLevelCapacity = teacher.LowerLevelCapacity
                        },
                        BelongingStudents = teacher.RegularStudents
                            .Where(s => s.GradeId == gradeId)
                            .Select(s => new StudentSimpleDTO
                            {
                                StudentId = s.StudentID,
                                Name = s.Name
                            })
                            .ToList(),
                        PreferenceCount = preferenceCounts.ContainsKey(teacher.Id) ? preferenceCounts[teacher.Id] : 0,
                        Description = teacher.Description,
                        IsAcceptTopStudent = teacher.AcceptsTopStudent,
                        IsChosenByTopStudent = teacher.BestStudentId.HasValue,
                    }).ToList();

                    // 将 BestStudent 加入 BelongingStudents
                    /*foreach (var dto in teacherDtos)
                    {
                        var teacher = teachers.First(t => t.Id == dto.Id);
                        if (teacher.BestStudent != null)
                        {
                            dto.BelongingStudents.Add(new StudentSimpleDTO
                            {
                                StudentId = teacher.BestStudent.StudentID,
                                Name = teacher.BestStudent.Name
                            });
                        }
                    }*/

                    response.success = true;
                    response.message = "获取教师列表成功。";
                    response.Data = teacherDtos;

                    return Ok(response);
                }
                // 如果用户是 Student
                else if (userRoles.Contains("Student"))
                {
                    // 从 JWT 中获取学生的 StudentID
                    var studentIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                    var studentID = studentIdClaim.Value;

                    // 获取学生对象
                    var student = await _dbContext.Students
                        .Include(s => s.Grade)
                        .FirstOrDefaultAsync(s => s.StudentID == studentID);

                    if (student == null)
                    {
                        response.success = false;
                        response.message = "学生不存在。";
                        return NotFound(response);
                    }

                    if (student.GradeId != gradeId)
                    {
                        response.success = false;
                        response.message = "您无法查看其他年级的教师列表。";
                        return BadRequest(response);
                    }

                    // 获取该年级的所有教师
                    var teachers = await _dbContext.Teachers
                        .Where(t => t.GradeId == gradeId)
                        .Include(t => t.BestStudent)
                        .ToListAsync();

                    // 获取该年级的所有学生，按 GPA 降序排序
                    var allStudents = await _dbContext.Students
                        .Where(s => s.GradeId == gradeId)
                        .OrderByDescending(s => s.GPA)
                        .ToListAsync();

                    // 计算学生的排名（从 1 开始）
                    int studentRank = allStudents.FindIndex(s => s.Id == student.Id) + 1;

                    // 计算顶尖学生的数量（接受顶尖学生的教师数量）
                    int topStudentCount = teachers.Count(t => t.AcceptsTopStudent);

                    // 计算三层学生的容量
                    int totalUpperCapacity = teachers.Sum(t => t.UpperLevelCapacity);
                    int totalMiddleCapacity = teachers.Sum(t => t.MiddleLevelCapacity);
                    int totalLowerCapacity = teachers.Sum(t => t.LowerLevelCapacity);

                    // 确定学生所在的组
                    string studentGroup = ""; // "Top"、"Upper"、"Middle"、"Lower"

                    if (studentRank <= topStudentCount)
                    {
                        studentGroup = "Top";
                    }
                    else
                    {
                        // 累计学生人数
                        int upperStartRank = topStudentCount + 1;
                        int upperEndRank = upperStartRank + totalUpperCapacity - 1;

                        int middleStartRank = upperEndRank + 1;
                        int middleEndRank = middleStartRank + totalMiddleCapacity - 1;

                        int lowerStartRank = middleEndRank + 1;
                        int lowerEndRank = lowerStartRank + totalLowerCapacity - 1;

                        if (studentRank >= upperStartRank && studentRank <= upperEndRank)
                        {
                            studentGroup = "Upper";
                        }
                        else if (studentRank >= middleStartRank && studentRank <= middleEndRank)
                        {
                            studentGroup = "Middle";
                        }
                        else if (studentRank >= lowerStartRank && studentRank <= lowerEndRank)
                        {
                            studentGroup = "Lower";
                        }
                        else
                        {
                            response.success = false;
                            response.message = "无法确定您的分组，可能是由于教师的容量设置不合理。";
                            return BadRequest(response);
                        }
                    }

                    // 获取学生所在组的学生列表
                    List<Student> groupStudents = new List<Student>();
                    if (studentGroup == "Upper")
                    {
                        groupStudents = allStudents.Skip(topStudentCount).Take(totalUpperCapacity).ToList();
                    }
                    else if (studentGroup == "Middle")
                    {
                        groupStudents = allStudents.Skip(topStudentCount + totalUpperCapacity).Take(totalMiddleCapacity).ToList();
                    }
                    else if (studentGroup == "Lower")
                    {
                        groupStudents = allStudents.Skip(topStudentCount + totalUpperCapacity + totalMiddleCapacity).Take(totalLowerCapacity).ToList();
                    }
                    else if (studentGroup == "Top")
                    {
                        // 顶尖学生
                        groupStudents = allStudents.Take(topStudentCount).ToList();
                    }

                    // 获取学生所在组的所有偏好
                    var groupStudentIds = groupStudents.Select(s => s.Id).ToList();

                    var preferences = await _dbContext.Preferences
                        .Where(p => groupStudentIds.Contains(p.StudentId))
                        .ToListAsync();

                    if (studentGroup == "Top")
                    {
                        teachers = teachers.Where(t => t.AcceptsTopStudent == true).ToList();
                    }

                    // 构建教师列表 DTO
                    var teacherDtos = teachers.Select(teacher =>
                    {
                        // 计算该教师在学生所在组的 PreferenceCount
                        int preferenceCount = 0;
                        if (studentGroup != "Top")
                        {
                            preferenceCount = preferences.Count(p => p.TeacherId == teacher.Id);
                        }

                        // 获取教师在该层次的容量
                        int levelCapacity = 0;
                        if (studentGroup == "Upper")
                        {
                            levelCapacity = teacher.UpperLevelCapacity;
                        }
                        else if (studentGroup == "Middle")
                        {
                            levelCapacity = teacher.MiddleLevelCapacity;
                        }
                        else if (studentGroup == "Lower")
                        {
                            levelCapacity = teacher.LowerLevelCapacity;
                        }
                        else if (studentGroup == "Top")
                        {
                            levelCapacity = teacher.AcceptsTopStudent ? 1 : 0;
                        }

                        return new TeacherDTO
                        {
                            Id = teacher.Id,
                            Name = teacher.Name,
                            GradeName = grade.Name,
                            LevelCapacities = new LevelCapacitiesDTO
                            {
                                UpperLevelCapacity = teacher.UpperLevelCapacity,
                                MiddleLevelCapacity = teacher.MiddleLevelCapacity,
                                LowerLevelCapacity = teacher.LowerLevelCapacity
                            },
                            LevelAvailableCapacity = levelCapacity,
                            BelongingStudents = null, // 学生不需要看到教师的学生列表
                            PreferenceCount = preferenceCount,
                            Description = teacher.Description,
                            IsAcceptTopStudent = teacher.AcceptsTopStudent,
                            IsChosenByTopStudent = teacher.BestStudentId.HasValue
                        };
                    }).ToList();

                    response.success = true;
                    response.message = "获取教师列表成功。";
                    response.Data = teacherDtos;

                    return Ok(response);
                }
                else
                {
                    response.success = false;
                    response.message = "您没有权限执行此操作。";
                    return Forbid();
                }
            }
            catch (Exception ex)
            {
                response.success = false;
                response.message = $"获取教师列表时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }


        /// <summary>
        /// 学生添加教师偏好
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("AddPreferences")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<StandardResponse>> AddPreferences([FromBody] AddPreferencesRequest request)
        {
            var response = new StandardResponse();

            // 获取学生 ID（从 JWT 中）
            var studentIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (studentIdClaim == null)
            {
                response.success = false;
                response.message = "无法获取学生身份信息。";
                return Unauthorized(response);
            }

            var studentIdString = studentIdClaim.Value;
            if (string.IsNullOrEmpty(studentIdString))
            {
                response.success = false;
                response.message = "学生身份信息无效。";
                return Unauthorized(response);
            }

            // StudentID 是字符串类型
            var studentID = studentIdString;

            // 开始数据库事务
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 查找学生
                var student = await _dbContext.Students
                    .Include(s => s.Preferences)
                    .FirstOrDefaultAsync(s => s.StudentID == studentID);

                if (student == null)
                {
                    await transaction.RollbackAsync();
                    response.success = false;
                    response.message = "未找到学生信息。";
                    return NotFound(response);
                }

                // 获取该年级的所有教师
                var teachers = await _dbContext.Teachers
                    .Where(t => t.GradeId == student.GradeId)
                    .ToListAsync();

                // 获取该年级的所有学生，按 GPA 降序排序
                var allStudents = await _dbContext.Students
                    .Where(s => s.GradeId == student.GradeId)
                    .OrderByDescending(s => s.GPA)
                    .ToListAsync();

                // 计算学生的排名（从 1 开始）
                int studentRank = allStudents.FindIndex(s => s.Id == student.Id) + 1;

                // 计算顶尖学生的数量（接受顶尖学生的教师数量）
                int topStudentCount = teachers.Count(t => t.AcceptsTopStudent);

                // 确定学生是否为顶尖学生
                bool isTopStudent = studentRank <= topStudentCount;

                if (isTopStudent == true)
                {
                    // 顶尖学生逻辑
                    // 验证输入
                    if (request.TeacherIds == null || request.TeacherIds.Count != 1)
                    {
                        response.success = false;
                        response.message = "作为顶尖学生，您必须选择一位导师。";
                        return BadRequest(response);
                    }

                    var teacherId = request.TeacherIds.First();

                    // 查找教师，确保教师存在且接受顶尖学生
                    var teacher = await _dbContext.Teachers
                        .FirstOrDefaultAsync(t => t.Id == teacherId && t.GradeId == student.GradeId && t.AcceptsTopStudent);

                    if (teacher == null)
                    {
                        response.success = false;
                        response.message = "所选导师不存在、与您不在同一年级或不接受顶尖学生。";
                        return BadRequest(response);
                    }

                    // 检查教师是否已被其他顶尖学生选择
                    if (teacher.BestStudentId != null)
                    {
                        response.success = false;
                        response.message = "该导师已被其他顶尖学生选择，请选择其他导师。";
                        return Conflict(response);
                    }

                    // 乐观并发控制
                    _dbContext.Entry(teacher).Property(t => t.BestStudentId).OriginalValue = teacher.BestStudentId;

                    // 设置教师的 BestStudentId 和学生的 AssignedTeacherId
                    teacher.BestStudentId = student.Id;
                    teacher.BestStudent = student;

                    student.AssignedTeacherId = teacher.Id;
                    student.AssignedTeacher = teacher;

                    // 保存更改
                    await _dbContext.SaveChangesAsync();

                    // 提交事务
                    await transaction.CommitAsync();

                    response.success = true;
                    response.message = "成功选择导师。";
                    return Ok(response);
                }
                else
                {
                    // 非顶尖学生逻辑
                    // 验证输入
                    if (request.TeacherIds == null || !request.TeacherIds.Any())
                    {
                        response.success = false;
                        response.message = "请至少选择一位导师。";
                        return BadRequest(response);
                    }

                    if (request.TeacherIds.Count > 3)
                    {
                        response.success = false;
                        response.message = "最多可以选择三位导师。";
                        return BadRequest(response);
                    }

                    // 删除现有偏好
                    if (student.Preferences != null && student.Preferences.Any())
                    {
                        _dbContext.Preferences.RemoveRange(student.Preferences);
                        await _dbContext.SaveChangesAsync();
                    }

                    // 验证教师 ID 列表
                    var teacherIds = request.TeacherIds.Distinct().ToList();

                    if (teacherIds.Count > 3)
                    {
                        response.success = false;
                        response.message = "导师数量不能超过三位。";
                        return BadRequest(response);
                    }

                    // 查找教师，确保教师存在且属于学生的年级
                    var selectedTeachers = await _dbContext.Teachers
                        .Where(t => teacherIds.Contains(t.Id) && t.GradeId == student.GradeId)
                        .ToListAsync();

                    if (selectedTeachers.Count != teacherIds.Count)
                    {
                        response.success = false;
                        response.message = "所选导师中有不存在或不属于您的年级。";
                        return BadRequest(response);
                    }

                    // 添加新偏好
                    var preferences = new List<Preference>();

                    for (int i = 0; i < teacherIds.Count; i++)
                    {
                        var preference = new Preference
                        {
                            StudentId = student.Id,
                            TeacherId = teacherIds[i],
                            PreferenceOrder = i + 1 // 偏好顺序从 1 开始
                        };
                        preferences.Add(preference);
                    }

                    _dbContext.Preferences.AddRange(preferences);
                    await _dbContext.SaveChangesAsync();

                    // 提交事务
                    await transaction.CommitAsync();

                    response.success = true;
                    response.message = "成功添加意向导师。";
                    return Ok(response);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                // 并发冲突处理
                await transaction.RollbackAsync();
                response.success = false;
                response.message = "提交失败，可能是因为所选导师已被其他Top学生选择，请重试。";
                return Conflict(response);
            }
            catch (Exception ex)
            {
                // 回滚事务
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"添加偏好导师时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }


        /// <summary>
        /// 学生获取自己的详细信息
        /// </summary>
        /// <returns></returns>
        [HttpGet("GetMyDetails")]
        [Authorize(Roles = "Student")]
        public async Task<ActionResult<StandardResponse>> GetMyDetails()
        {
            // 获取学生的 StudentID（从 JWT 中的 NameIdentifier 声明）
            var studentIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (studentIdClaim == null || string.IsNullOrEmpty(studentIdClaim.Value))
            {
                var response = new StandardResponse
                {
                    success = false,
                    message = "无法获取学生身份信息。"
                };
                return Unauthorized(response);
            }

            var studentID = studentIdClaim.Value;

            // 查询学生信息，包含关联的年级、已分配的教师和偏好
            var student = await _dbContext.Students
                .Include(s => s.Grade)
                .Include(s => s.AssignedTeacher)
                .Include(s => s.Preferences.OrderBy(p => p.PreferenceOrder))
                    .ThenInclude(p => p.Teacher)
                .FirstOrDefaultAsync(s => s.StudentID == studentID);

            if (student == null)
            {
                var response = new StandardResponse
                {
                    success = false,
                    message = "未找到学生信息。"
                };
                return NotFound(response);
            }

            var gradeId = student.GradeId;

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

            // 获取所有学生，按 GPA 降序排序
            var allStudentsInGrade = await _dbContext.Students
                .Where(s => s.GradeId == gradeId)
                .OrderByDescending(s => s.GPA)
                .ToListAsync();

            // 计算 GPA 排名，处理相同 GPA 的情况
            var gpaGroups = allStudentsInGrade
                .GroupBy(s => s.GPA)
                .OrderByDescending(g => g.Key)
                .ToList();

            var overallRank = 1;
            var studentsWithLevelAndRank = new List<(Student student, int GPARank, string Level, int LevelRank)>();

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

                foreach (var s in studentsInGroup)
                {
                    studentsWithLevelAndRank.Add((s, overallRank, level, levelRankCounter));
                    levelRankCounter++;
                }

                // 更新该层次的 levelRank 计数器
                levelRankCounters[level] = levelRankCounter;

                overallRank += groupCount;
            }

            // 查找当前学生的数据
            var currentStudentData = studentsWithLevelAndRank.FirstOrDefault(s => s.student.Id == student.Id);

            if (currentStudentData == default)
            {
                var response = new StandardResponse
                {
                    success = false,
                    message = "无法找到学生的等级信息。"
                };
                return BadRequest(response);
            }

            // 构建 StudentDetailDTO 对象
            var studentDetailDTO = new StudentDetailDTO
            {
                Id = student.Id,
                StudentID = student.StudentID,
                Name = student.Name,
                GPA = student.GPA,
                GPARank = currentStudentData.GPARank,
                Level = currentStudentData.Level,
                LevelRank = currentStudentData.LevelRank,
                AssignedTeacher = student.AssignedTeacher != null
                    ? new AssignedTeacherDTO
                    {
                        Id = student.AssignedTeacher.Id,
                        Name = student.AssignedTeacher.Name
                    }
                    : null,
                PreferenceTeacherNames = student.Preferences
                    .Select(p => p.Teacher.Name)
                    .ToList(),
                GradeId = student.Grade.Id,
                GradeName = student.Grade.Name
            };

            var responseSuccess = new StandardResponse
            {
                success = true,
                message = "获取学生信息成功。",
                Data = studentDetailDTO
            };

            return Ok(responseSuccess);
        }


        /// <summary>
        /// 按志愿分配
        /// </summary>
        /// <param name="gradeId"></param>
        /// <returns></returns>
        [HttpPost("AssignTeachers")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> AssignTeachers([FromQuery] long gradeId)
        {
            var response = new StandardResponse();

            // 验证 gradeId 是否有效
            if (gradeId <= 0)
            {
                response.success = false;
                response.message = "年级 ID 无效。";
                return BadRequest(response);
            }

            // 检查年级是否存在
            var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Id == gradeId);
            if (grade == null)
            {
                response.success = false;
                response.message = $"未找到 ID 为 {gradeId} 的年级。";
                return NotFound(response);
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 获取指定年级的所有教师，包括他们是否接受顶尖学生的信息
                var teachersInGrade = await _dbContext.Teachers
                    .Where(t => t.GradeId == gradeId)
                    .Include(t => t.BestStudent)
                    .ToListAsync();

                // 获取所有学生，按 GPA 降序排序，包含偏好和已分配教师
                var allStudentsInGrade = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId)
                    .Include(s => s.Preferences)
                    .Include(s => s.AssignedTeacher)
                    .OrderByDescending(s => s.GPA)
                    .ToListAsync();

                // 计算顶尖学生数量（接受顶尖学生的教师数量）
                int topStudentCount = teachersInGrade.Count(t => t.AcceptsTopStudent);

                // 确定顶尖学生列表
                var topStudents = allStudentsInGrade.Take(topStudentCount).ToList();

                // 非顶尖学生列表
                var nonTopStudents = allStudentsInGrade.Skip(topStudentCount).ToList();

                // 清除非顶尖学生的已分配教师
                foreach (var student in nonTopStudents)
                {
                    student.AssignedTeacherId = null;
                }
                await _dbContext.SaveChangesAsync();

                // 初始化教师的层次容量映射
                var teacherLevelCapacities = teachersInGrade.ToDictionary(t => t.Id, t => new Dictionary<string, int>
        {
            { "Upper", t.UpperLevelCapacity },
            { "Middle", t.MiddleLevelCapacity },
            { "Lower", t.LowerLevelCapacity }
        });

                // 初始化教师的层次已分配数量
                var teacherLevelAssignedCounts = teachersInGrade.ToDictionary(t => t.Id, t => new Dictionary<string, int>
        {
            { "Upper", 0 },
            { "Middle", 0 },
            { "Lower", 0 }
        });

                // 已分配的学生集合（非顶尖学生）
                var assignedStudentIds = new HashSet<long>();

                // 未提交任何教师偏好的学生名单
                var studentsWithNoPreferences = nonTopStudents
                    .Where(s => s.Preferences == null || !s.Preferences.Any())
                    .ToList();

                // 提交了偏好但未被分配的学生名单
                var studentsWithPreferencesButUnassigned = new List<Student>();

                // 从非顶尖学生中移除未提交偏好的学生
                var studentsWithPreferences = nonTopStudents.Except(studentsWithNoPreferences).ToList();

                // 计算各层次的容量
                int totalUpperCapacity = teachersInGrade.Sum(t => t.UpperLevelCapacity);
                int totalMiddleCapacity = teachersInGrade.Sum(t => t.MiddleLevelCapacity);
                int totalLowerCapacity = teachersInGrade.Sum(t => t.LowerLevelCapacity);

                // 初始化各层次剩余容量
                int remainingUpperCapacity = totalUpperCapacity;
                int remainingMiddleCapacity = totalMiddleCapacity;
                int remainingLowerCapacity = totalLowerCapacity;

                // 分组 GPA，相同 GPA 的学生在一起
                var gpaGroups = studentsWithPreferences
                    .GroupBy(s => s.GPA)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                var studentsWithLevel = new List<(Student Student, string Level)>();

                // 分配非顶尖学生到层次
                foreach (var group in gpaGroups)
                {
                    var studentsInGroup = group.ToList();
                    int groupCount = studentsInGroup.Count;

                    string level = null;

                    if (remainingUpperCapacity >= groupCount)
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
                        level = "Unassigned";
                    }

                    foreach (var student in studentsInGroup)
                    {
                        studentsWithLevel.Add((student, level));
                        // 这里可以选择将 level 存储在临时变量中，不需要修改 Student 模型
                    }
                }

                // 按层次处理学生分配
                foreach (var level in new[] { "Upper", "Middle", "Lower" })
                {
                    var studentsInLevel = studentsWithLevel
                        .Where(s => s.Level == level)
                        .Select(s => s.Student)
                        .OrderByDescending(s => s.GPA)
                        .ToList();

                    foreach (var student in studentsInLevel)
                    {
                        if (assignedStudentIds.Contains(student.Id))
                        {
                            continue; // 已经分配，跳过
                        }

                        bool assigned = false;

                        // 学生的偏好教师列表，按 PreferenceOrder 排序
                        var preferences = student.Preferences
                            .OrderBy(p => p.PreferenceOrder)
                            .ToList();

                        foreach (var preference in preferences)
                        {
                            var teacherId = preference.TeacherId;

                            // 检查教师是否在指定年级
                            if (!teacherLevelCapacities.ContainsKey(teacherId))
                            {
                                continue; // 教师不在指定年级，跳过
                            }

                            // 检查教师在该层次是否还有容量
                            var capacities = teacherLevelCapacities[teacherId];
                            var assignedCounts = teacherLevelAssignedCounts[teacherId];

                            int availableSlots = capacities[level] - assignedCounts[level];

                            if (availableSlots > 0)
                            {
                                // 分配学生到教师
                                student.AssignedTeacherId = teacherId;
                                assignedStudentIds.Add(student.Id);

                                // 更新教师的已分配数量
                                teacherLevelAssignedCounts[teacherId][level]++;

                                assigned = true;
                                break; // 跳出偏好循环
                            }
                        }

                        // 如果所有偏好教师都没有可用容量，学生保持未分配
                        if (!assigned)
                        {
                            studentsWithPreferencesButUnassigned.Add(student);
                        }
                    }
                }

                // 保存对学生分配的更改
                await _dbContext.SaveChangesAsync();

                // 提交事务
                await transaction.CommitAsync();

                // 构建响应消息
                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine("教师分配完成。");

                if (studentsWithNoPreferences.Any())
                {
                    var names = string.Join("，", studentsWithNoPreferences.Select(s => s.Name));
                    messageBuilder.AppendLine($"以下 {studentsWithNoPreferences.Count} 位学生未提交任何教师偏好：{names}。");
                }

                if (studentsWithPreferencesButUnassigned.Any())
                {
                    var names = string.Join("，", studentsWithPreferencesButUnassigned.Select(s => s.Name));
                    messageBuilder.AppendLine($"以下 {studentsWithPreferencesButUnassigned.Count} 位学生提交了教师偏好但未被分配到任何教师：{names}。");
                }

                response.success = true;
                response.message = messageBuilder.ToString();
                return Ok(response);
            }
            catch (Exception ex)
            {
                // 回滚事务
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"教师分配时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }

        /*public async Task<ActionResult<StandardResponse>> AssignTeachers([FromQuery] long gradeId)
        {
            var response = new StandardResponse();

            // 验证 gradeId 是否有效
            if (gradeId <= 0)
            {
                response.success = false;
                response.message = "年级 ID 无效。";
                return BadRequest(response);
            }

            // 检查年级是否存在
            var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Id == gradeId);
            if (grade == null)
            {
                response.success = false;
                response.message = $"未找到 ID 为 {gradeId} 的年级。";
                return NotFound(response);
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 开始数据库事务
                
                var studentsInGrade = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId)
                    .ToListAsync();
                foreach (var student in studentsInGrade)
                {
                    student.AssignedTeacherId = null;
                }
                await _dbContext.SaveChangesAsync();


                // 获取指定年级的教师，初始化教师容量映射和已分配学生数量
                var teachersInGrade = await _dbContext.Teachers
                    .Where(t => t.GradeId == gradeId)
                    .ToListAsync();
                var teacherCapacityMap = teachersInGrade.ToDictionary(t => t.Id, t => t.MaxCapacity);
                var teacherAssignedCount = teachersInGrade.ToDictionary(t => t.Id, t => 0);

                // 已分配的学生集合
                var assignedStudentIds = new HashSet<long>();

                // 识别未选择任何教师的学生
                var studentsWithNoPreferences = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId && !s.Preferences.Any())
                    .ToListAsync();

                var studentsWithNoPreferencesNames = studentsWithNoPreferences.Select(s => s.Name).ToList();
                var numberOfStudentsWithNoPreferences = studentsWithNoPreferences.Count;


                // 遍历偏好顺序，从 1 到 3
                for (int preferenceOrder = 1; preferenceOrder <= 3; preferenceOrder++)
                {
                    // 获取当前偏好顺序的所有偏好，排除已分配的学生，并过滤指定年级
                    var preferences = await _dbContext.Preferences
                        .Where(p => p.PreferenceOrder == preferenceOrder
                                    && !assignedStudentIds.Contains(p.StudentId)
                                    && p.Student.GradeId == gradeId)
                        .Include(p => p.Student)
                        .Include(p => p.Teacher)
                        .ToListAsync();

                    // 按教师分组偏好
                    var preferencesByTeacher = preferences.GroupBy(p => p.TeacherId);

                    foreach (var teacherGroup in preferencesByTeacher)
                    {
                        var teacherId = teacherGroup.Key;

                        // 检查教师是否在指定年级
                        if (!teacherCapacityMap.ContainsKey(teacherId))
                        {
                            continue; // 教师不在指定年级，跳过
                        }

                        var teacherPreferences = teacherGroup.ToList();

                        // 检查教师是否还有容量
                        var maxCapacity = teacherCapacityMap[teacherId];
                        var currentAssigned = teacherAssignedCount[teacherId];
                        var availableSlots = maxCapacity - currentAssigned;

                        if (availableSlots <= 0)
                        {
                            continue; // 教师已满
                        }

                        // 获取未分配的学生列表，按 GPA 降序排列
                        var studentsToConsider = teacherPreferences
                            .Where(p => !assignedStudentIds.Contains(p.StudentId))
                            .Select(p => p.Student)
                            .OrderByDescending(s => s.GPA)
                            .ToList();

                        foreach (var student in studentsToConsider)
                        {
                            if (availableSlots <= 0)
                            {
                                break; // 教师已满
                            }

                            // 分配学生
                            student.AssignedTeacherId = teacherId;
                            assignedStudentIds.Add(student.Id);

                            // 更新教师的已分配学生数量
                            teacherAssignedCount[teacherId]++;
                            availableSlots--;
                        }
                    }
                }

                // 保存对学生分配的更改
                await _dbContext.SaveChangesAsync();

                // 提交事务
                await transaction.CommitAsync();

                // 构建响应消息
                if (numberOfStudentsWithNoPreferences > 0)
                {
                    response.message = $"教师分配完成。年级 '{grade.Name}' 中有 {numberOfStudentsWithNoPreferences} 位学生未选择任何教师：{string.Join('，', studentsWithNoPreferencesNames)}。";
                }
                else
                {
                    response.message = $"教师分配完成。年级 '{grade.Name}' 的所有学生均已选择偏好教师。";
                }

                response.success = true;
                return Ok(response);
            }
            catch (Exception ex)
            {
                // 回滚事务
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"教师分配时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }*/

        /// <summary>
        /// 全自动分配
        /// </summary>
        /// <param name="gradeId"></param>
        /// <returns></returns>
        [HttpPost("AssignTeachersWithAutoAssign")]
        [Authorize(Roles = "Admin")]
        /*public async Task<ActionResult<StandardResponse>> AssignTeachersWithAutoAssign([FromQuery] long gradeId)
        {
            var response = new StandardResponse();

            // 验证 gradeId 是否有效
            if (gradeId <= 0)
            {
                response.success = false;
                response.message = "年级 ID 无效。";
                return BadRequest(response);
            }

            // 检查年级是否存在
            var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Id == gradeId);
            if (grade == null)
            {
                response.success = false;
                response.message = $"未找到 ID 为 {gradeId} 的年级。";
                return NotFound(response);
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 开始数据库事务
                

                // 清除指定年级学生的教师分配
                var studentsInGrade = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId)
                    .ToListAsync();
                foreach (var student in studentsInGrade)
                {
                    student.AssignedTeacherId = null;
                }
                await _dbContext.SaveChangesAsync();

                // **2. 初始化数据结构**

                // 获取指定年级的教师，初始化教师容量映射和已分配学生数量
                var teachersInGrade = await _dbContext.Teachers
                    .Where(t => t.GradeId == gradeId)
                    .ToListAsync();
                var teacherCapacityMap = teachersInGrade.ToDictionary(t => t.Id, t => t.MaxCapacity);
                var teacherAssignedCount = teachersInGrade.ToDictionary(t => t.Id, t => 0);

                // 已分配的学生集合
                var assignedStudentIds = new HashSet<long>();

                // 识别未选择任何教师的学生
                var studentsWithNoPreferences = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId && !s.Preferences.Any())
                    .ToListAsync();

                var studentsWithNoPreferencesNames = studentsWithNoPreferences.Select(s => s.Name).ToList();
                var numberOfStudentsWithNoPreferences = studentsWithNoPreferences.Count;

                // 按照偏好顺序和 GPA 进行分配

                // 遍历偏好顺序，从 1 到 3
                for (int preferenceOrder = 1; preferenceOrder <= 3; preferenceOrder++)
                {
                    // 获取当前偏好顺序的所有偏好，排除已分配的学生，并过滤指定年级
                    var preferences = await _dbContext.Preferences
                        .Where(p => p.PreferenceOrder == preferenceOrder
                                    && !assignedStudentIds.Contains(p.StudentId)
                                    && p.Student.GradeId == gradeId)
                        .Include(p => p.Student)
                        .Include(p => p.Teacher)
                        .ToListAsync();

                    // 按教师分组偏好
                    var preferencesByTeacher = preferences.GroupBy(p => p.TeacherId);

                    foreach (var teacherGroup in preferencesByTeacher)
                    {
                        var teacherId = teacherGroup.Key;

                        // 检查教师是否在指定年级
                        if (!teacherCapacityMap.ContainsKey(teacherId))
                        {
                            continue; // 教师不在指定年级，跳过
                        }

                        var teacherPreferences = teacherGroup.ToList();

                        // 检查教师是否还有容量
                        var maxCapacity = teacherCapacityMap[teacherId];
                        var currentAssigned = teacherAssignedCount[teacherId];
                        var availableSlots = maxCapacity - currentAssigned;

                        if (availableSlots <= 0)
                        {
                            continue; // 教师已满
                        }

                        // 获取未分配的学生列表，按 GPA 降序排列
                        var studentsToConsider = teacherPreferences
                            .Where(p => !assignedStudentIds.Contains(p.StudentId))
                            .Select(p => p.Student)
                            .OrderByDescending(s => s.GPA)
                            .ToList();

                        foreach (var student in studentsToConsider)
                        {
                            if (availableSlots <= 0)
                            {
                                break; // 教师已满
                            }

                            // 分配学生
                            student.AssignedTeacherId = teacherId;
                            assignedStudentIds.Add(student.Id);

                            // 更新教师的已分配学生数量
                            teacherAssignedCount[teacherId]++;
                            availableSlots--;
                        }
                    }
                }

                // 为未能匹配的学生进行自动分配

                // 获取未分配的学生列表（包括有偏好未分配和无偏好的学生）
                var unassignedStudents = studentsInGrade
                    .Where(s => !assignedStudentIds.Contains(s.Id))
                    .OrderByDescending(s => s.GPA)
                    .ToList();

                foreach (var student in unassignedStudents)
                {
                    // 获取有剩余容量的教师列表
                    var availableTeachers = teacherCapacityMap
                        .Where(t => teacherAssignedCount[t.Key] < t.Value)
                        .Select(t => t.Key)
                        .ToList();

                    if (availableTeachers.Any())
                    {
                        // 分配到第一个有空位的教师
                        var teacherId = availableTeachers.First();

                        // 分配学生
                        student.AssignedTeacherId = teacherId;
                        assignedStudentIds.Add(student.Id);

                        // 更新教师的已分配学生数量
                        teacherAssignedCount[teacherId]++;
                    }
                    else
                    {
                        // 没有可用的教师，学生无法被分配
                        // AssignedTeacherId 保持为 null
                    }
                }

                // 保存更改并返回结果

                // 保存对学生分配的更改
                await _dbContext.SaveChangesAsync();

                // 提交事务
                await transaction.CommitAsync();

                // 统计未能成功分配的学生
                var numberOfUnassignedStudents = studentsInGrade.Count - assignedStudentIds.Count;
                var unassignedStudentNames = studentsInGrade
                    .Where(s => !assignedStudentIds.Contains(s.Id))
                    .Select(s => s.Name)
                    .ToList();

                // 构建响应消息
                if (numberOfUnassignedStudents > 0)
                {
                    response.message = $"教师分配完成。年级 '{grade.Name}' 中有 {numberOfUnassignedStudents} 位学生未能成功分配导师：{string.Join('，', unassignedStudentNames)}。";
                }
                else
                {
                    response.message = $"教师分配完成。年级 '{grade.Name}' 的所有学生均已成功分配导师。";
                }

                response.success = true;
                return Ok(response);
            }
            catch (Exception ex)
            {
                // 回滚事务
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"教师分配时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }*/
        public async Task<ActionResult<StandardResponse>> AssignTeachersWithAutoAssign([FromQuery] long gradeId)
        {
            var response = new StandardResponse();

            // 验证 gradeId 是否有效
            if (gradeId <= 0)
            {
                response.success = false;
                response.message = "年级 ID 无效。";
                return BadRequest(response);
            }

            // 检查年级是否存在
            var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Id == gradeId);
            if (grade == null)
            {
                response.success = false;
                response.message = $"未找到 ID 为 {gradeId} 的年级。";
                return NotFound(response);
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 获取指定年级的所有教师，包括他们是否接受顶尖学生的信息
                var teachersInGrade = await _dbContext.Teachers
                    .Where(t => t.GradeId == gradeId)
                    .Include(t => t.BestStudent)
                    .ToListAsync();

                // 获取所有学生，按 GPA 降序排序，包含偏好和已分配教师
                var allStudentsInGrade = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId)
                    .Include(s => s.Preferences)
                    .Include(s => s.AssignedTeacher)
                    .OrderByDescending(s => s.GPA)
                    .ToListAsync();

                // 计算顶尖学生数量（接受顶尖学生的教师数量）
                int topStudentCount = teachersInGrade.Count(t => t.AcceptsTopStudent);

                // 确定顶尖学生列表
                var topStudents = allStudentsInGrade.Take(topStudentCount).ToList();

                // 非顶尖学生列表
                var nonTopStudents = allStudentsInGrade.Skip(topStudentCount).ToList();

                // 清除非顶尖学生的已分配教师
                foreach (var student in nonTopStudents)
                {
                    student.AssignedTeacherId = null;
                }
                await _dbContext.SaveChangesAsync();

                // 初始化教师的层次容量映射
                var teacherLevelCapacities = teachersInGrade.ToDictionary(t => t.Id, t => new Dictionary<string, int>
                {
                    { "Upper", t.UpperLevelCapacity },
                    { "Middle", t.MiddleLevelCapacity },
                    { "Lower", t.LowerLevelCapacity }
                });

                // 初始化教师的层次已分配数量
                var teacherLevelAssignedCounts = teachersInGrade.ToDictionary(t => t.Id, t => new Dictionary<string, int>
                {
                    { "Upper", 0 },
                    { "Middle", 0 },
                    { "Lower", 0 }
                });

                // 已分配的学生集合（非顶尖学生）
                var assignedStudentIds = new HashSet<long>();

                // 未提交任何教师偏好的学生名单
                var studentsWithNoPreferences = nonTopStudents
                    .Where(s => s.Preferences == null || !s.Preferences.Any())
                    .ToList();

                // 提交了偏好但未被分配的学生名单
                var studentsWithPreferencesButUnassigned = new List<Student>();

                // 提交了偏好的学生列表
                var studentsWithPreferences = nonTopStudents.Except(studentsWithNoPreferences).ToList();

                // 计算各层次的容量
                int totalUpperCapacity = teachersInGrade.Sum(t => t.UpperLevelCapacity);
                int totalMiddleCapacity = teachersInGrade.Sum(t => t.MiddleLevelCapacity);
                int totalLowerCapacity = teachersInGrade.Sum(t => t.LowerLevelCapacity);

                // 初始化各层次剩余容量
                int remainingUpperCapacity = totalUpperCapacity;
                int remainingMiddleCapacity = totalMiddleCapacity;
                int remainingLowerCapacity = totalLowerCapacity;

                // 分组 GPA，相同 GPA 的学生在一起
                var gpaGroups = nonTopStudents
                    .GroupBy(s => s.GPA)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                var studentsWithLevel = new List<(Student Student, string Level)>();

                // 分配非顶尖学生到层次
                foreach (var group in gpaGroups)
                {
                    var studentsInGroup = group.ToList();
                    int groupCount = studentsInGroup.Count;

                    string level = null;

                    if (remainingUpperCapacity >= groupCount)
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
                        level = "Unassigned"; // 理论上不应该发生
                    }

                    foreach (var student in studentsInGroup)
                    {
                        studentsWithLevel.Add((student, level));
                        // 可以选择将 level 存储在临时变量中，不需要修改 Student 模型
                    }
                }

                // 按层次处理学生分配
                foreach (var level in new[] { "Upper", "Middle", "Lower" })
                {
                    var studentsInLevel = studentsWithLevel
                        .Where(s => s.Level == level)
                        .Select(s => s.Student)
                        .OrderByDescending(s => s.GPA)
                        .ToList();

                    foreach (var student in studentsInLevel)
                    {
                        if (assignedStudentIds.Contains(student.Id))
                        {
                            continue; // 已经分配，跳过
                        }

                        bool assigned = false;

                        // 学生的偏好教师列表，按 PreferenceOrder 排序
                        var preferences = student.Preferences
                            .OrderBy(p => p.PreferenceOrder)
                            .ToList();

                        foreach (var preference in preferences)
                        {
                            var teacherId = preference.TeacherId;

                            // 检查教师是否在指定年级
                            if (!teacherLevelCapacities.ContainsKey(teacherId))
                            {
                                continue; // 教师不在指定年级，跳过
                            }

                            // 检查教师在该层次是否还有容量
                            var capacities = teacherLevelCapacities[teacherId];
                            var assignedCounts = teacherLevelAssignedCounts[teacherId];

                            int availableSlots = capacities[level] - assignedCounts[level];

                            if (availableSlots > 0)
                            {
                                // 分配学生到教师
                                student.AssignedTeacherId = teacherId;
                                assignedStudentIds.Add(student.Id);

                                // 更新教师的已分配数量
                                teacherLevelAssignedCounts[teacherId][level]++;

                                assigned = true;
                                break; // 跳出偏好循环
                            }
                        }

                        // 如果所有偏好教师都没有可用容量，学生保持未分配，稍后自动分配
                        if (!assigned)
                        {
                            studentsWithPreferencesButUnassigned.Add(student);
                        }
                    }
                }

                // 将未分配的学生（包括未提交偏好和偏好未满足的）合并到一个列表
                var unassignedStudents = studentsWithPreferencesButUnassigned.ToList();
                unassignedStudents.AddRange(studentsWithNoPreferences);

                // 为未分配的学生自动分配教师
                foreach (var studentLevelPair in studentsWithLevel.Where(s => unassignedStudents.Contains(s.Student)))
                {
                    var student = studentLevelPair.Student;
                    var level = studentLevelPair.Level;

                    if (assignedStudentIds.Contains(student.Id))
                    {
                        continue; // 已经分配，跳过
                    }

                    // 查找在该层次有可用容量的教师
                    var availableTeacherId = teacherLevelCapacities
                        .Where(t => t.Value[level] - teacherLevelAssignedCounts[t.Key][level] > 0)
                        .Select(t => t.Key)
                        .FirstOrDefault();

                    if (availableTeacherId != 0)
                    {
                        // 分配学生到该教师
                        student.AssignedTeacherId = availableTeacherId;
                        assignedStudentIds.Add(student.Id);

                        // 更新教师的已分配数量
                        teacherLevelAssignedCounts[availableTeacherId][level]++;
                    }
                    else
                    {
                        // 理论上不应该发生，如果发生了，学生保持未分配
                    }
                }

                // 保存对学生分配的更改
                await _dbContext.SaveChangesAsync();

                // 提交事务
                await transaction.CommitAsync();

                response.success = true;
                response.message = "教师分配完成。所有学生已成功分配到教师。";
                return Ok(response);
            }
            catch (Exception ex)
            {
                // 回滚事务
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"教师分配时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }

        /// <summary>
        /// 清除该年级全部分配
        /// </summary>
        /// <param name="gradeId"></param>
        /// <returns></returns>
        [HttpPost("ClearTeacherAssignments")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<StandardResponse>> ClearTeacherAssignments([FromQuery] long gradeId)
        {
            var response = new StandardResponse();

            // 验证 gradeId 是否有效
            if (gradeId <= 0)
            {
                response.success = false;
                response.message = "年级 ID 无效。";
                return BadRequest(response);
            }

            // 检查年级是否存在
            var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Id == gradeId);
            if (grade == null)
            {
                response.success = false;
                response.message = $"未找到 ID 为 {gradeId} 的年级。";
                return NotFound(response);
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 清除该年级的所有学生的 AssignedTeacherId
                var studentsInGrade = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId)
                    .ToListAsync();

                foreach (var student in studentsInGrade)
                {
                    student.AssignedTeacherId = null;
                }

                // 清除该年级的所有教师的 BestStudentId
                var teachersInGrade = await _dbContext.Teachers
                    .Where(t => t.GradeId == gradeId)
                    .ToListAsync();

                foreach (var teacher in teachersInGrade)
                {
                    teacher.BestStudentId = null;
                }

                await _dbContext.SaveChangesAsync();

                await transaction.CommitAsync();

                response.success = true;
                response.message = $"已成功清除年级 '{grade.Name}' 的所有学生和教师的分配。";
                return Ok(response);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                response.success = false;
                response.message = $"清除分配时发生错误：{ex.Message}";
                return StatusCode(500, response);
            }
        }


        /// <summary>
        /// 导出Excel表格
        /// </summary>
        /// <param name="gradeId"></param>
        /// <returns></returns>
        [HttpGet("ExportGradeData")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ExportGradeData([FromQuery] long gradeId)
        {
            // 验证 gradeId 是否有效
            if (gradeId <= 0)
            {
                var response = new StandardResponse
                {
                    success = false,
                    message = "年级 ID 无效。"
                };
                return BadRequest(response);
            }

            // 检查年级是否存在
            var grade = await _dbContext.Grades.FirstOrDefaultAsync(g => g.Id == gradeId);
            if (grade == null)
            {
                var response = new StandardResponse
                {
                    success = false,
                    message = $"未找到 ID 为 {gradeId} 的年级。"
                };
                return NotFound(response);
            }

            try
            {
                // 创建一个新的 Excel 工作簿
                using var workbook = new XLWorkbook();

                // -----------------------
                // 第一张表：学生表
                // -----------------------
                var studentWorksheet = workbook.Worksheets.Add("学生表");

                // 添加表头
                studentWorksheet.Cell(1, 1).Value = "姓名";
                studentWorksheet.Cell(1, 2).Value = "学号";
                studentWorksheet.Cell(1, 3).Value = "GPA排名";
                studentWorksheet.Cell(1, 4).Value = "分组";
                studentWorksheet.Cell(1, 5).Value = "偏好导师名";
                studentWorksheet.Cell(1, 6).Value = "分配导师名";

                // 获取学生数据
                var students = await _dbContext.Students
                    .Where(s => s.GradeId == gradeId)
                    .Include(s => s.Preferences.OrderBy(p => p.PreferenceOrder))
                        .ThenInclude(p => p.Teacher)
                    .Include(s => s.AssignedTeacher)
                    .ToListAsync();

                // 计算 GPA 排名，处理相同 GPA 的情况
                var gpaGroups = students
                    .GroupBy(s => s.GPA)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                var rank = 1;
                var studentsWithRank = new List<(Student Student, int Rank)>();

                foreach (var group in gpaGroups)
                {
                    foreach (var student in group)
                    {
                        studentsWithRank.Add((Student: student, Rank: rank));
                    }
                    rank += group.Count();
                }

                // 计算学生的层次（Level）
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

                var studentsWithLevelAndRank = new List<(Student Student, int GPARank, string Level, int LevelRank)>();

                // 按 GPA 排名排序学生
                var studentsOrderedByRank = studentsWithRank.OrderBy(sr => sr.Rank).ToList();

                foreach (var group in studentsOrderedByRank.GroupBy(sr => sr.Student.GPA).OrderByDescending(g => g.Key))
                {
                    var groupStudents = group.ToList();
                    int groupCount = groupStudents.Count;

                    string level = null;

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
                        level = "Unassigned";
                    }

                    int levelRankCounter = levelRankCounters[level];

                    foreach (var sr in groupStudents)
                    {
                        studentsWithLevelAndRank.Add((sr.Student, sr.Rank, level, levelRankCounter));
                        levelRankCounter++;
                    }

                    levelRankCounters[level] = levelRankCounter;
                }

                // 填充学生数据
                int studentRow = 2;
                foreach (var sr in studentsWithLevelAndRank)
                {
                    studentWorksheet.Cell(studentRow, 1).Value = sr.Student.Name;
                    studentWorksheet.Cell(studentRow, 2).Value = sr.Student.StudentID;
                    studentWorksheet.Cell(studentRow, 3).Value = sr.GPARank;
                    studentWorksheet.Cell(studentRow, 4).Value = sr.Level;
                    // 偏好导师名（用逗号分隔）
                    var preferenceTeacherNames = string.Join("，", sr.Student.Preferences.Select(p => p.Teacher.Name));
                    studentWorksheet.Cell(studentRow, 5).Value = preferenceTeacherNames;
                    // 分配导师名
                    var assignedTeacherName = sr.Student.AssignedTeacher?.Name ?? "";
                    studentWorksheet.Cell(studentRow, 6).Value = assignedTeacherName;

                    studentRow++;
                }

                // 设置列宽
                studentWorksheet.Columns().AdjustToContents();

                // -----------------------
                // 第二张表：教师表
                // -----------------------
                var teacherWorksheet = workbook.Worksheets.Add("教师表");

                // 添加表头
                teacherWorksheet.Cell(1, 1).Value = "姓名";
                teacherWorksheet.Cell(1, 2).Value = "分配学生";

                // 获取教师数据
                var teachers = await _dbContext.Teachers
                    .Where(t => t.GradeId == gradeId)
                    .Include(t => t.BestStudent)
                    .Include(t => t.RegularStudents)
                    .ToListAsync();

                // 填充教师数据
                int teacherRow = 2;
                foreach (var teacher in teachers)
                {
                    teacherWorksheet.Cell(teacherRow, 1).Value = teacher.Name;
                    // 分配学生姓名（包括顶尖学生和常规学生，用逗号分隔）
                    var assignedStudents = new List<string>();
                    if (teacher.BestStudent != null)
                    {
                        assignedStudents.Add(teacher.BestStudent.Name);
                    }
                    if (teacher.RegularStudents != null && teacher.RegularStudents.Any())
                    {
                        assignedStudents.AddRange(teacher.RegularStudents.Select(s => s.Name));
                    }
                    var assignedStudentNames = string.Join("，", assignedStudents);
                    teacherWorksheet.Cell(teacherRow, 2).Value = assignedStudentNames;

                    teacherRow++;
                }

                // 设置列宽
                teacherWorksheet.Columns().AdjustToContents();

                // -----------------------
                // 返回 Excel 文件
                // -----------------------
                var stream = new MemoryStream();
                workbook.SaveAs(stream);
                stream.Position = 0;

                string fileName = $"年级_{grade.Name}_数据.xlsx";

                return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                var response = new StandardResponse
                {
                    success = false,
                    message = $"导出数据时发生错误：{ex.Message}"
                };
                return StatusCode(500, response);
            }
        }
    }
}