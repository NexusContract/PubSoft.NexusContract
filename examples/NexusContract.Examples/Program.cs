using System;

namespace PubSoft.NexusContract.Examples
{
    /// <summary>
    /// Examples workspace for NexusContract framework.
    /// Contracts and business logic should be defined in this project.
    /// Framework providers are in src/Providers/*.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  NexusContract Framework");
            Console.WriteLine("========================================\n");

            Console.WriteLine("Framework layers:");
            Console.WriteLine("  1. Abstractions  (IApiRequest, Attributes)");
            Console.WriteLine("  2. Core          (NexusGateway, NexusProxyEndpoint)");
            Console.WriteLine("  3. Providers     (Alipay, UnionPay adapters)");
            Console.WriteLine("\nApplication layers:");
            Console.WriteLine("  • Contracts (netstandard2.0) - Define business requests/responses");
            Console.WriteLine("  • Endpoints - Subclass AlipayProxyEndpoint/NexusProxyEndpoint");
            Console.WriteLine("  • Main app - Orchestrate contracts + endpoints");

            Console.WriteLine("\nAdd your application examples here.");
        }
    }
}
