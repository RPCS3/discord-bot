using System;
using System.Collections.Generic;

namespace CompatApiClient.Utils
{
    public static class Statistics
    {
        public static long Mean(this IEnumerable<long> data)
        {
            System.Numerics.BigInteger sum = 0;
            var itemCount = 0;
            foreach (var value in data)
            {
                sum += value;
                itemCount ++;
            }
            if (itemCount == 0)
                throw new ArgumentException("Sequence must contain elements", nameof(data));

            return (long)(sum / itemCount);
        }

        public static double StdDev(this IEnumerable<long> data)
        {
            System.Numerics.BigInteger σx = 0, σx2 = 0;
            var n = 0;
            foreach (var value in data)
            {
                σx += value;
                σx2 += (System.Numerics.BigInteger)value * value;
                n++;
            }
            if (n == 0)
                throw new ArgumentException("Sequence must contain elements", nameof(data));

            var σ2 = σx * σx;
            return Math.Sqrt((double)((n * σx2) - σ2) / ((n - 1) * n));
        }
    }
}