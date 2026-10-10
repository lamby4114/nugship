using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OreSeas.Net
{
    /// <summary>
    /// Thin System.Net.WebSockets wrapper. Receive runs on a worker task and only enqueues raw text; the owner drains
    /// Inbox on the Unity main thread (ServerClient.Update), so no Unity API is touched off-thread.
    /// </summary>
    public sealed class WsClient : IDisposable
    {
        ClientWebSocket ws; CancellationTokenSource cts;
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        public readonly ConcurrentQueue<string> Inbox = new ConcurrentQueue<string>();
        volatile bool open, closed; volatile string closeReason; volatile int closeCode;

        public bool IsOpen => open && ws != null && ws.State == WebSocketState.Open;
        public bool Closed => closed;
        public string CloseReason => closeReason;
        /// <summary>WebSocket close status from the server (1012 = Service Restart during a deploy), 0 if none.</summary>
        public int CloseCode => closeCode;

        public async Task<bool> Connect(string url, int timeoutMs = 6000)
        {
            Dispose();
            closed = false; open = false; closeReason = null; closeCode = 0;
            cts = new CancellationTokenSource();
            ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            try
            {
                var connect = ws.ConnectAsync(new Uri(url), cts.Token);
                if (await Task.WhenAny(connect, Task.Delay(timeoutMs)) != connect) { closeReason = "timeout"; cts.Cancel(); closed = true; return false; }
                await connect; // rethrows connect errors
                open = true;
                _ = Task.Run(ReceiveLoop);
                return true;
            }
            catch (Exception e) { closeReason = e.GetBaseException().Message; closed = true; return false; }
        }

        async Task ReceiveLoop()
        {
            var buf = new byte[16 * 1024]; var sb = new StringBuilder();
            try
            {
                while (!cts.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var seg = new ArraySegment<byte>(buf);
                    var r = await ws.ReceiveAsync(seg, cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) { closeCode = r.CloseStatus.HasValue ? (int)r.CloseStatus.Value : 0; closeReason = "server closed " + r.CloseStatus; break; }
                    sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    if (r.EndOfMessage) { Inbox.Enqueue(sb.ToString()); sb.Length = 0; }
                }
            }
            catch (Exception e) { if (closeReason == null) closeReason = e.GetBaseException().Message; }
            finally { open = false; closed = true; }
        }

        public async void Send(string text)
        {
            if (!IsOpen) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            await sendLock.WaitAsync();
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token); }
            catch (Exception e) { Debug.LogWarning("[OreSeas.Net] send failed: " + e.GetBaseException().Message); }
            finally { sendLock.Release(); }
        }

        public void Dispose()
        {
            open = false;
            try { cts?.Cancel(); } catch { }
            try { if (ws != null && ws.State == WebSocketState.Open) _ = ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
            try { ws?.Dispose(); } catch { }
            ws = null; cts = null;
        }
    }
}
