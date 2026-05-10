using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace ImageServer
{
    public class ImageWebServer
    {
        private readonly int _port;
        private readonly string _rootPath;
        private readonly RequestQueue _queue = new RequestQueue();
        private readonly ImageCache _cache;
        private readonly int _threadCount;
        private readonly HttpListener _listener;
        
        // Za sprečavanje Cache Stampede problema
        private readonly Dictionary<string, object> _activeFetches = new Dictionary<string, object>();

        public ImageWebServer(int port, string rootPath, int cacheSize, int threadCount)
        {
            _port = port;
            _rootPath = rootPath;
            _cache = new ImageCache(cacheSize);
            _threadCount = threadCount;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
        }

        public void Start()
        {
            _listener.Start();
            Logger.Log($"Server pokrenut na portu {_port}. Pretraga u: {_rootPath}");

            // Inicijalizacija Pool-a radnih niti
            for (int i = 0; i < _threadCount; i++)
            {
                Thread t = new Thread(WorkerLoop) { IsBackground = true, Name = $"Worker-{i}" };
                t.Start();
            }

            // Glavna nit samo prima zahteve i stavlja ih u red
            while (_listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    _queue.Enqueue(context);
                }
                catch (Exception ex) { Logger.Error($"Greška pri prijemu: {ex.Message}"); }
            }
        }

        private void WorkerLoop()
        {
            while (true)
            {
                HttpListenerContext context = _queue.Dequeue();
                ProcessRequest(context);
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            string? fileName = context.Request.Url?.AbsolutePath.TrimStart('/');
            HttpListenerResponse response = context.Response;

            if (string.IsNullOrEmpty(fileName))
            {
                SendResponse(response, "Greška: Navedite ime fajla u URL-u.", 400);
                return;
            }

            // 1. Provera keša
            if (_cache.TryGet(fileName, out byte[]? data))
            {
                Logger.Log($"[HIT] Slika '{fileName}' poslata iz keša.");
                SendImage(response, data!, fileName);
                return;
            }

            // 2. Rešavanje Cache Stampede
            object fileLock;
            lock (_activeFetches)
            {
                if (!_activeFetches.TryGetValue(fileName, out fileLock!))
                {
                    fileLock = new object();
                    _activeFetches[fileName] = fileLock;
                }
            }

            // Samo jedna nit će ući ovde za određeni fajl, ostale čekaju
            lock (fileLock)
            {
                // Dupla provera: možda je nit koja je bila pre nas upravo napunila keš
                if (_cache.TryGet(fileName, out data))
                {
                    SendImage(response, data!, fileName);
                    return;
                }

                Logger.Log($"[MISS] Fajl '{fileName}' se pribavlja sa diska.");
                data = FindAndLoadImage(fileName);

                if (data != null)
                {
                    _cache.Add(fileName, data);
                    SendImage(response, data, fileName);
                }
                else
                {
                    SendResponse(response, $"Fajl '{fileName}' nije pronadjen ili tip nije dozvoljen.", 404);
                }

                // Sklanjamo lock objekt iz rečnika nakon obrade
                lock (_activeFetches) { _activeFetches.Remove(fileName); }
            }
        }

        private byte[]? FindAndLoadImage(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLower();
            string[] allowedExts = { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };

            if (!allowedExts.Contains(ext)) return null;

            try
            {
                // Rekurzivna pretraga (SearchOption.AllDirectories)
                var files = Directory.GetFiles(_rootPath, fileName, SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    return File.ReadAllBytes(files[0]);
                }
            }
            catch (Exception ex) { Logger.Error($"Fajl sistem greška: {ex.Message}"); }
            return null;
        }

        private void SendImage(HttpListenerResponse response, byte[] data, string fileName)
        {
            try
            {
                string ext = Path.GetExtension(fileName).TrimStart('.').ToLower();
                response.ContentType = $"image/{ext}";
                response.ContentLength64 = data.Length;
                response.OutputStream.Write(data, 0, data.Length);
                response.OutputStream.Close();
            }
            catch { /* Konekcija prekinuta */ }
        }

        private void SendResponse(HttpListenerResponse response, string message, int statusCode)
        {
            try
            {
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(message);
                response.StatusCode = statusCode;
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch { }
        }
    }
}