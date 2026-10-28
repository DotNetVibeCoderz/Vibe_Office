// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ExcelNet.Formulas;

/// <summary>
/// PMT, PV, FV, NPER, RATE, NPV, IRR, XNPV and XIRR, with Excel's conventions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Signs are cash flow, not magnitude.</b> Money paid out is negative and money received is
/// positive, so the payment on a loan you receive is negative: <c>PMT(8%/12, 10, 10000)</c> is
/// −1,037.03. Returning the magnitude "because that is the payment" is the classic way to get every
/// downstream formula wrong by a sign.
/// </para>
/// <para>
/// <b>The iterative ones answer <c>#NUM!</c> rather than a guess.</b> RATE, IRR and XIRR solve for a
/// rate with Newton's method. When it does not converge — cash flows that never change sign, or a
/// start too far from any root — Excel says <c>#NUM!</c>, and so does this. Returning the last
/// iterate would put a confident, wrong number in a financial model.
/// </para>
/// </remarks>
internal static class FinancialFunctions
{
    private const int MaxIterations = 100;
    private const double Tolerance = 1e-10;

    internal static CellValue Pmt(double rate, double nper, double pv, double fv, double type)
    {
        if (nper == 0)
        {
            return CellValue.FromError("#NUM!");
        }

        if (rate == 0)
        {
            return CellValue.FromNumber(-(pv + fv) / nper);
        }

        var growth = Math.Pow(1 + rate, nper);
        return CellValue.FromNumber(-(rate * (pv * growth + fv)) / ((1 + rate * type) * (growth - 1)));
    }

    internal static CellValue Fv(double rate, double nper, double pmt, double pv, double type)
    {
        if (rate == 0)
        {
            return CellValue.FromNumber(-(pv + pmt * nper));
        }

        var growth = Math.Pow(1 + rate, nper);
        return CellValue.FromNumber(-(pv * growth + pmt * (1 + rate * type) * (growth - 1) / rate));
    }

    internal static CellValue Pv(double rate, double nper, double pmt, double fv, double type)
    {
        if (rate == 0)
        {
            return CellValue.FromNumber(-(fv + pmt * nper));
        }

        var growth = Math.Pow(1 + rate, nper);
        return CellValue.FromNumber(-(fv + pmt * (1 + rate * type) * (growth - 1) / rate) / growth);
    }

    internal static CellValue Nper(double rate, double pmt, double pv, double fv, double type)
    {
        if (rate == 0)
        {
            return pmt == 0 ? CellValue.FromError("#NUM!") : CellValue.FromNumber(-(pv + fv) / pmt);
        }

        var adjusted = pmt * (1 + rate * type);
        var ratio = (adjusted - fv * rate) / (adjusted + pv * rate);

        // A payment that never covers the interest has no number of periods.
        return ratio <= 0 || double.IsNaN(ratio)
            ? CellValue.FromError("#NUM!")
            : CellValue.FromNumber(Math.Log(ratio) / Math.Log(1 + rate));
    }

    internal static CellValue Rate(double nper, double pmt, double pv, double fv, double type, double guess)
    {
        // The future value of everything at rate r, which is zero at the answer.
        double F(double r) => Math.Abs(r) < 1e-12
            ? pv + pmt * nper + fv
            : pv * Math.Pow(1 + r, nper) + pmt * (1 + r * type) * (Math.Pow(1 + r, nper) - 1) / r + fv;

        return Solve(F, guess);
    }

    /// <summary>NPV: the first value is discounted one period, not zero — unlike IRR's.</summary>
    internal static CellValue Npv(double rate, IReadOnlyList<double> values)
    {
        if (rate == -1)
        {
            return CellValue.FromError("#DIV/0!");
        }

        var total = 0.0;

        for (var i = 0; i < values.Count; i++)
        {
            total += values[i] / Math.Pow(1 + rate, i + 1);
        }

        return CellValue.FromNumber(total);
    }

    internal static CellValue Irr(IReadOnlyList<double> values, double guess)
    {
        if (!values.Any(v => v > 0) || !values.Any(v => v < 0))
        {
            return CellValue.FromError("#NUM!");
        }

        double F(double r)
        {
            var total = 0.0;

            for (var i = 0; i < values.Count; i++)
            {
                total += values[i] / Math.Pow(1 + r, i);
            }

            return total;
        }

        return Solve(F, guess);
    }

    internal static CellValue Xnpv(double rate, IReadOnlyList<double> values, IReadOnlyList<double> dates)
    {
        if (values.Count != dates.Count || values.Count == 0)
        {
            return CellValue.FromError("#NUM!");
        }

        return CellValue.FromNumber(XnpvAt(rate, values, dates));
    }

    internal static CellValue Xirr(IReadOnlyList<double> values, IReadOnlyList<double> dates, double guess)
    {
        if (values.Count != dates.Count || !values.Any(v => v > 0) || !values.Any(v => v < 0))
        {
            return CellValue.FromError("#NUM!");
        }

        return Solve(r => XnpvAt(r, values, dates), guess);
    }

    /// <summary>Discounts by actual days over 365, from the first date — Excel's XNPV convention.</summary>
    private static double XnpvAt(double rate, IReadOnlyList<double> values, IReadOnlyList<double> dates)
    {
        var total = 0.0;

        for (var i = 0; i < values.Count; i++)
        {
            total += values[i] / Math.Pow(1 + rate, (dates[i] - dates[0]) / 365.0);
        }

        return total;
    }

    /// <summary>
    /// Newton's method with a numerical derivative, refusing anything that does not settle.
    /// </summary>
    private static CellValue Solve(Func<double, double> f, double guess)
    {
        var r = guess;

        for (var i = 0; i < MaxIterations; i++)
        {
            var value = f(r);

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                break;
            }

            var step = Math.Max(1e-7, Math.Abs(r) * 1e-7);
            var derivative = (f(r + step) - f(r - step)) / (2 * step);

            if (derivative == 0 || double.IsNaN(derivative))
            {
                break;
            }

            var next = r - value / derivative;

            // A rate at or below -100% means losing more than everything; there is no root there.
            if (next <= -1)
            {
                next = (r - 1) / 2;
            }

            if (Math.Abs(next - r) < Tolerance)
            {
                return CellValue.FromNumber(next);
            }

            r = next;
        }

        return CellValue.FromError("#NUM!");
    }
}
