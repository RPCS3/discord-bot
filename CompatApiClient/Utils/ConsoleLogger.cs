using System;
using System.Net.Http;

namespace CompatApiClient.Utils
{
    public static class ConsoleLogger
    {
        public static void PrintError(Exception e, HttpResponseMessage response, ConsoleColor color = ConsoleColor.Red)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("HTTP error: " + e);
            if (response != null)
            {
                try
                {
                    var msg = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Console.ResetColor();
                    Console.WriteLine(response.RequestMessage.RequestUri);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(msg);
                }
                catch { }
            }
            Console.ResetColor();
        }
    }
}
