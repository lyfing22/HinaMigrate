using DataMigrate.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DataMigrate.Sources.MongoDb;

/// <summary>
/// MongoDB exam.approval 集合文档实体
/// </summary>
[BsonIgnoreExtraElements]
public class ExamEntity
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Id { get; set; } = "";

    public Guid GlobalId { get; set; }
    public string OrgCode { get; set; } = "";
    public string OrgName { get; set; } = "";
    public string ExamId { get; set; } = "";
    public string Status { get; set; } = "";
    public string ReportStoragePath { get; set; } = "";
    public DateTime CreateTime { get; set; }
    public DateTime UpdateTime { get; set; }
    public bool IsMatch { get; set; }
    public bool IsStudyUploaded { get; set; }
    public string AppId { get; set; } = "";

    public ExamOrderInfo? Order { get; set; }
    public ExamVisitInfo? Visit { get; set; }
    public ExamPatientInfo? Patient { get; set; }
    public ExamReportInfo? Report { get; set; }

    public static string GetId(string orgCode, string examId) => $"{orgCode}|{examId}";
}
