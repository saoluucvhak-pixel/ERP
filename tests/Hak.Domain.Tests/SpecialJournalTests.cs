using Hak.Domain.Ledger;
using Hak.Domain.Vouchers;

namespace Hak.Domain.Tests;

public class SpecialJournalTests
{
    private static readonly Guid Cash = Guid.NewGuid(), Bank = Guid.NewGuid(), Inv = Guid.NewGuid(), Rev = Guid.NewGuid(), Ar = Guid.NewGuid(), Adv = Guid.NewGuid(), Exp = Guid.NewGuid(), Ap = Guid.NewGuid();
    private static readonly SpecialJournalContext Ctx = new(new HashSet<Guid> { Cash, Bank }, new HashSet<Guid> { Inv }, new HashSet<Guid> { Rev }, new HashSet<Guid> { Ap, Adv });

    private static IReadOnlyList<SpecialJournal> Touch(string type, params (Guid, Side)[] lines) => SpecialJournals.Touched(type, lines, Ctx);

    [Fact]
    public void CashWithdrawal_IsBothReceiptAndPayment_RecordedOnceInReceipts()
    {
        var t = Touch(VoucherTypes.General, (Cash, Side.Debit), (Bank, Side.Credit));   // rút tiền gửi nhập quỹ
        Assert.Equal([SpecialJournal.CashReceipts, SpecialJournal.CashPayments], t);
        Assert.Equal(SpecialJournal.CashReceipts, SpecialJournals.Main(t));
    }

    [Fact]
    public void AdvanceSettlementForMaterials_WithCashRefund_IsReceiptAndPurchase_MainIsCashReceipts()
    {
        var t = Touch(VoucherTypes.General, (Inv, Side.Debit), (Cash, Side.Debit), (Adv, Side.Credit));   // hoàn ứng mua vật tư kèm hoàn tiền thừa
        Assert.Equal([SpecialJournal.CashReceipts, SpecialJournal.Purchases], t);
        Assert.Equal(SpecialJournal.CashReceipts, SpecialJournals.Main(t));      // ưu tiên tiền mặt
    }

    [Fact]
    public void SinglePurposeEntries_AreNotDuplicates()
    {
        Assert.Equal([SpecialJournal.CashReceipts], Touch(VoucherTypes.General, (Cash, Side.Debit), (Ar, Side.Credit)));
        Assert.Equal([SpecialJournal.CashPayments], Touch(VoucherTypes.General, (Exp, Side.Debit), (Cash, Side.Credit)));
        Assert.Equal([SpecialJournal.Sales], Touch(VoucherTypes.General, (Ar, Side.Debit), (Rev, Side.Credit)));
        Assert.Equal([SpecialJournal.Purchases], Touch(VoucherTypes.General, (Inv, Side.Debit), (Ap, Side.Credit)));
        Assert.Equal([SpecialJournal.Purchases], Touch(VoucherTypes.PurchaseInvoice, (Exp, Side.Debit), (Ap, Side.Credit)));   // hóa đơn mua dịch vụ
        Assert.Empty(Touch(VoucherTypes.General, (Exp, Side.Debit), (Ap, Side.Credit)));
        Assert.Equal(SpecialJournal.General, SpecialJournals.Main([]));
    }

    [Fact]
    public void ProductionReceipt_AndOpeningAndClosing_AreNotPurchases()
    {
        Assert.Empty(Touch(VoucherTypes.General, (Inv, Side.Debit), (Exp, Side.Credit)));                 // nhập thành phẩm: đối ứng chi phí, không phải công nợ / tiền
        Assert.Empty(Touch(VoucherTypes.OpeningBalance, (Cash, Side.Debit), (Rev, Side.Credit)));
        Assert.Empty(Touch(VoucherTypes.Closing, (Rev, Side.Debit), (Cash, Side.Credit)));
    }
}
