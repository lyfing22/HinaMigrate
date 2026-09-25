using System.Text.Json.Serialization;

namespace DataMigrate.Models;

public class ExamUploadReq
{
    public string OrgCode { get; set; } = "";
    public string OrgName { get; set; } = "";
    public string ExamId { get; set; } = "";
    public ExamOrderInfo Order { get; set; } = new();
    public ExamVisitInfo Visit { get; set; } = new();
    public ExamPatientInfo Patient { get; set; } = new();
    public ExamReportInfo Report { get; set; } = new();
}

public class ExamOrderInfo
{
    public string? HisOrderCode { get; set; }
    public string? HisRequestDate { get; set; }
    public string? HisExamName { get; set; }
    public string? AccessionNumber { get; set; }
    public string? BodyParts { get; set; }
    public string? BodyPartCodes { get; set; }
    public string? CheckItems { get; set; }
    public string? CheckItemsCodes { get; set; }
    public string? Modality { get; set; }
    public string? ModalityCode { get; set; }
    public int? ImageCount { get; set; }
    public string? ApplyDepartment { get; set; }
    public string? ApplyDepartmentCode { get; set; }
    public string? ApplyDoctor { get; set; }
    public string? ApplyDoctorCode { get; set; }
    public DateTime? ApplyTime { get; set; }
    public string? Department { get; set; }
    public string? DepartmentCode { get; set; }
    public string? Doctor { get; set; }
    public string? DoctorCode { get; set; }
    public DateTime? ArriveTime { get; set; }
    public DateTime? StudyTime { get; set; }

    [JsonPropertyName("studyInstanceUID")]
    public string? StudyInstanceUID { get; set; }

    [JsonPropertyName("hiImgIndx")]
    public string? HiImgIndx { get; set; }
}

public class ExamVisitInfo
{
    public string? ClinicalNumber { get; set; }
    public string? PatientType { get; set; }
    public string? RoomNumber { get; set; }
    public string? BedNumber { get; set; }
    public string? InpatientNumber { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? ClinicalDiagnosis { get; set; }
    public string? PastHistory { get; set; }
    public string? AllergyHistory { get; set; }
    public string? Symptom { get; set; }
    public string? Sign { get; set; }
    public string? LaboratoryReport { get; set; }
    public string? OtherDiagnosis { get; set; }
    public string? DiseaseHistory { get; set; }
}

public class ExamPatientInfo
{
    public string? PatientId { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Name { get; set; }
    public string? SocietyNumber { get; set; }
    public string? SocietyNumberCity { get; set; }
    public string? AddressCity { get; set; }
    public string? IdNo { get; set; }
    public string? IdNoType { get; set; }
    public string? Address { get; set; }
    public string? Telephone { get; set; }
    public int? Age { get; set; }
    public string AgeUnit { get; set; } = "year";
    public string? AgeDisplay { get; set; }
}

public class ExamReportInfo
{
    public string? Findings { get; set; }
    public string? Impression { get; set; }
    public string? TechParams { get; set; }
    public string? ReportDoctor { get; set; }
    public string? ReportDoctorCode { get; set; }
    public DateTime? ReportTime { get; set; }
    public string? ApproveDoctor { get; set; }
    public string? ApproveDoctorCode { get; set; }
    public DateTime? ApproveTime { get; set; }
    public int? PositiveStatus { get; set; }
    public string? ReportUrl { get; set; }
    public string? RejectDoctor { get; set; }
    public string? RejectDoctorCode { get; set; }
    public DateTime? RejectTime { get; set; }
    public DateTime? ReportReceiveTime { get; set; }
    public string? AssignedReportDoctor { get; set; }
}

public class ExamUploadWithPdfReq
{
    public ExamUploadReq Data { get; set; } = new();
    public string PdfReport { get; set; } = "";
}

public class RisDataUploadRes
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

/// <summary>ZTemp_MigratePlan 记录映射（仅查询 Id/StartTime/EndTime 用于断点续跑匹配）</summary>
public class MigratePlanRecord
{
    public Guid Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int? TotalRecords { get; set; }
    public int? SuccessCount { get; set; }
    public int? FailedCount { get; set; }
    public string Status { get; set; } = MigratePlanStatus.Pending.ToString();
}
