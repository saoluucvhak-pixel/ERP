namespace Hak.Domain.Catalog;

/// <summary>Loại danh mục được phân nhóm. Mỗi loại có cây nhóm riêng.</summary>
public enum GroupDomain { Partner = 1, Item = 2, Asset = 3, Warehouse = 4 }

/// <summary>
/// Nhóm danh mục nhiều cấp (nhóm cha – nhóm con) để phân loại và quản lý theo nhóm: nhóm đối tượng (khách hàng theo khu vực, ngành, nhà cung cấp theo loại hàng…),
/// nhóm vật tư, nhóm tài sản / CCDC, nhóm kho. Một mục thuộc được NHIỀU nhóm (<see cref="CatalogGroupMember"/>); lọc / báo cáo theo nhóm cha gồm cả mọi nhóm con.
/// Nhóm chỉ để phân tích và lọc, không ảnh hưởng số liệu đã ghi sổ.
/// </summary>
public sealed class CatalogGroup
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public GroupDomain Domain { get; set; }
    /// <summary>Mã nhóm (chữ hoa, số, dấu -), duy nhất trong công ty và loại danh mục.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }
    /// <summary>Mặc định cho nhóm đối tượng: hạn mức tín dụng (VND) và số ngày hạn thanh toán, dùng khi chính đối tượng chưa khai báo. Xem GroupDefaults.</summary>
    public decimal? DefaultCreditLimit { get; set; }
    public int? DefaultPaymentTermsDays { get; set; }
    /// <summary>Mặc định cho nhóm vật tư: thuế suất GTGT (%) khi vật tư chưa khai.</summary>
    public decimal? DefaultVatRate { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

/// <summary>Một mục (đối tượng, vật tư, tài sản, kho) thuộc một nhóm.</summary>
public sealed class CatalogGroupMember
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid GroupId { get; set; }
    public Guid EntityId { get; set; }
    /// <summary>Nhóm chính của mục (như nhóm hàng / danh mục chính ở SAP, Odoo, ERPNext): mặc định theo nhóm lấy từ nhóm chính. Mục chỉ ở một nhóm thì nhóm đó mặc nhiên là nhóm chính.</summary>
    public bool IsPrimary { get; set; }
}
