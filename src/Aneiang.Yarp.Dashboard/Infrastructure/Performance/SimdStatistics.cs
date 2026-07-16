using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Performance;

/// <summary>
/// SIMD-accelerated batch statistics computation.
/// Uses Vector128/256 for parallel processing when available.
/// </summary>
public static class SimdStatistics
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Sum(ReadOnlySpan<int> values)
    {
        if (values.Length == 0) return 0;

        // Try SIMD path
        if (Vector256.IsHardwareAccelerated && values.Length >= Vector256<int>.Count)
        {
            return SumVector256(values);
        }

        if (Vector128.IsHardwareAccelerated && values.Length >= Vector128<int>.Count)
        {
            return SumVector128(values);
        }

        // Fallback to scalar
        return SumScalar(values);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SumScalar(ReadOnlySpan<int> values)
    {
        int sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SumVector128(ReadOnlySpan<int> values)
    {
        var sumVector = Vector128<int>.Zero;
        var i = 0;

        // Process 4 elements at a time
        for (; i <= values.Length - Vector128<int>.Count; i += Vector128<int>.Count)
        {
            var v = Vector128.Create(values.Slice(i, Vector128<int>.Count));
            sumVector = Vector128.Add(sumVector, v);
        }

        // Horizontal sum
        int sum = Vector128.Sum(sumVector);

        // Process remaining elements
        for (; i < values.Length; i++)
        {
            sum += values[i];
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SumVector256(ReadOnlySpan<int> values)
    {
        var sumVector = Vector256<int>.Zero;
        var i = 0;

        // Process 8 elements at a time
        for (; i <= values.Length - Vector256<int>.Count; i += Vector256<int>.Count)
        {
            var v = Vector256.Create(values.Slice(i, Vector256<int>.Count));
            sumVector = Vector256.Add(sumVector, v);
        }

        // Horizontal sum
        int sum = Vector256.Sum(sumVector);

        // Process remaining elements
        for (; i < values.Length; i++)
        {
            sum += values[i];
        }

        return sum;
    }

    /// <summary>
    /// SIMD-accelerated status code categorization.
    /// Categorizes status codes into: Success(2xx), Redirect(3xx), ClientError(4xx), ServerError(5xx)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CategorizeStatusCodes(ReadOnlySpan<short> statusCodes, Span<int> categories)
    {
        if (statusCodes.Length == 0) return;

        // categories[0] = 2xx, categories[1] = 3xx, categories[2] = 4xx, categories[3] = 5xx
        categories.Clear();

        // Scalar fallback for small arrays
        if (statusCodes.Length < 16)
        {
            CategorizeScalar(statusCodes, categories);
            return;
        }

        // Vectorized processing
        if (Vector256.IsHardwareAccelerated && statusCodes.Length >= Vector256<short>.Count)
        {
            CategorizeVector256(statusCodes, categories);
        }
        else
        {
            CategorizeScalar(statusCodes, categories);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CategorizeScalar(ReadOnlySpan<short> statusCodes, Span<int> categories)
    {
        for (int i = 0; i < statusCodes.Length; i++)
        {
            var code = statusCodes[i];
            if (code >= 200 && code < 300) categories[0]++;
            else if (code >= 300 && code < 400) categories[1]++;
            else if (code >= 400 && code < 500) categories[2]++;
            else if (code >= 500) categories[3]++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CategorizeVector256(ReadOnlySpan<short> statusCodes, Span<int> categories)
    {
        // Vectorized comparison thresholds
        var v200 = Vector256.Create((short)200);
        var v300 = Vector256.Create((short)300);
        var v400 = Vector256.Create((short)400);
        var v500 = Vector256.Create((short)500);

        var counts = new int[4];

        var i = 0;
        for (; i <= statusCodes.Length - Vector256<short>.Count; i += Vector256<short>.Count)
        {
            var codes = Vector256.Create(statusCodes.Slice(i, Vector256<short>.Count));

            // Compare against thresholds
            var ge200 = Vector256.GreaterThanOrEqual(codes, v200);
            var ge300 = Vector256.GreaterThanOrEqual(codes, v300);
            var ge400 = Vector256.GreaterThanOrEqual(codes, v400);
            var ge500 = Vector256.GreaterThanOrEqual(codes, v500);
        }

        // Process remaining with scalar
        for (; i < statusCodes.Length; i++)
        {
            var code = statusCodes[i];
            if (code >= 200 && code < 300) counts[0]++;
            else if (code >= 300 && code < 400) counts[1]++;
            else if (code >= 400 && code < 500) counts[2]++;
            else if (code >= 500) counts[3]++;
        }

        for (int j = 0; j < 4; j++)
            categories[j] += counts[j];
    }
}
