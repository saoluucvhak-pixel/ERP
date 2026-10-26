namespace Hak.Domain.Tax;

/// <summary>Normalizes evidence without changing the imported values or inferring the sign from the ledger being checked.</summary>
public static class EInvoiceAmounts
{
    public static (decimal Amount, decimal Vat)? SignedAdjustment(EInvoiceRecord invoice, EInvoiceAdjustNature verifiedLegacyNature = EInvoiceAdjustNature.None)
        => invoice.AmountConvention switch
        {
            EInvoiceAmountConvention.Signed => (invoice.AmountBeforeVat, invoice.VatAmount),
            EInvoiceAmountConvention.MagnitudeWithNature when invoice.AdjustmentNature == EInvoiceAdjustNature.Decrease
                => (-invoice.AmountBeforeVat, -invoice.VatAmount),
            EInvoiceAmountConvention.MagnitudeWithNature when invoice.AdjustmentNature == EInvoiceAdjustNature.Increase
                => (invoice.AmountBeforeVat, invoice.VatAmount),
            EInvoiceAmountConvention.Unknown when verifiedLegacyNature == EInvoiceAdjustNature.Decrease
                => (-invoice.AmountBeforeVat, -invoice.VatAmount),
            EInvoiceAmountConvention.Unknown when verifiedLegacyNature == EInvoiceAdjustNature.Increase
                => (invoice.AmountBeforeVat, invoice.VatAmount),
            // Negative legacy evidence already carries an unambiguous sign. Positive legacy evidence may mean either increase or decrease.
            EInvoiceAmountConvention.Unknown when invoice.AmountBeforeVat <= 0 && invoice.VatAmount <= 0 && invoice.TotalPayment < 0
                => (invoice.AmountBeforeVat, invoice.VatAmount),
            _ => null,
        };
}
