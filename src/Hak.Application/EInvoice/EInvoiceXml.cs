using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Hak.Domain.Tax;

namespace Hak.Application.EInvoice;

/// <summary>
/// Dựng bản XML nháp gửi nhà cung cấp. Cấu trúc này là cấu trúc NỘI BỘ của HAK (đủ các trường dữ liệu bắt buộc của hóa đơn và thông tin liên kết hóa đơn gốc);
/// nhà cung cấp chuyển sang lược đồ chính thức của cơ quan thuế khi ký [CẦN XÁC MINH lược đồ và các trường theo quy định hiện hành].
/// Ký hiệu, số, ngày ký, mã cơ quan thuế để trống cho nhà cung cấp điền.
/// </summary>
public static class EInvoiceXml
{
    public static string Build(ProviderIssueRequest r)
    {
        static string N(decimal v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        var doc = new XDocument(new XElement("HDon",
            new XElement("TTChung",
                new XElement("KyHieuMau", r.TemplateCode), new XElement("KyHieu"), new XElement("SoHoaDon"), new XElement("NgayLap", r.IssueDate.ToString("yyyy-MM-dd")),
                new XElement("NgayKy"), new XElement("MaCQT"), new XElement("DVTTe", r.Currency), r.ExchangeRate is { } rate ? new XElement("TGia", N(rate)) : null,
                new XElement("LoaiHD", r.Relation.ToString()),
                r.Relation == EInvoiceRelation.Original ? null : new XElement("HDLienQuan",
                    new XElement("KyHieuMau", r.RelatedTemplate), new XElement("KyHieu", r.RelatedSeries), new XElement("SoHoaDon", r.RelatedNumber),
                    r.RelatedDate is { } d ? new XElement("NgayLap", d.ToString("yyyy-MM-dd")) : null,
                    new XElement("TinhChat", r.Relation == EInvoiceRelation.Replacement ? "ThayThe" : r.Nature.ToString()),
                    new XElement("LyDo", r.Reason))),
            new XElement("NBan", new XElement("Ten", r.Seller.Name), new XElement("MST", r.Seller.TaxCode), new XElement("DChi", r.Seller.Address)),
            new XElement("NMua", new XElement("Ten", r.Buyer.Name), new XElement("MST", r.Buyer.TaxCode), new XElement("DChi", r.Buyer.Address)),
            new XElement("DSHHDVu", r.Lines.Select(l => new XElement("HHDVu", new XElement("STT", l.LineNo), new XElement("THHDVu", l.Name), new XElement("DVTinh", l.Unit),
                new XElement("SLuong", N(l.Quantity)), new XElement("DGia", N(l.UnitPrice)), new XElement("STCKhau", N(l.Discount)), new XElement("ThTien", N(l.Amount)),
                new XElement("TSuat", l.VatRate is { } v ? N(v) + "%" : "KCT"), new XElement("TThue", N(l.VatAmount))))),
            new XElement("TToan", new XElement("TgTCThue", N(r.AmountBeforeVat)), new XElement("TgTThue", N(r.VatAmount)), new XElement("TgTTTBSo", N(r.Total)))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }

    public static string Sha256(string xml) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
}
