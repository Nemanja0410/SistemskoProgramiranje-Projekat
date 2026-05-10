using System;
using System.IO;

namespace ImageServer
{
    class Program
    {
        static void Main(string[] args)
        {
            // Konfiguracija
            int port = 5050;
            int maxCacheItems = 5; // Ograničenje veličine keša na 5 slika
            int workerThreads = 4; // Maksimalno 4 paralelne obrade
            
            // Putanja do root foldera (Slike folder unutar Debug/Release foldera)
            string rootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Slike");

            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
                // Napravi podfolder za testiranje rekurzije
                Directory.CreateDirectory(Path.Combine(rootPath, "Podfolder"));
            }

            ImageWebServer server = new ImageWebServer(port, rootPath, maxCacheItems, workerThreads);
            
            Console.WriteLine("=== SISTEMSKO PROGRAMIRANJE: IMAGE SERVER ===");
            server.Start();
        }
    }
}