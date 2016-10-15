using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
namespace Bend.Util {

    public class HttpProcessor {
        public TcpClient socket;        
        public HttpServer srv;
        public static Process proc;
        private Stream inputStream;
        public StreamWriter outputStream;

        public String http_method;
        public String http_url;
        public String http_protocol_versionstring;
        public Hashtable httpHeaders = new Hashtable();
        public Hashtable cookies = new Hashtable();


        private static int MAX_POST_SIZE = 10 * 1024 * 1024; // 10MB

        public HttpProcessor(TcpClient s, HttpServer srv) {
            this.socket = s;
            this.srv = srv;                   
        }
        private string streamReadLine(Stream inputStream) {
            int next_char;
            string data = "";
            while (true) {
                next_char = inputStream.ReadByte();
                if (next_char == '\n') { break; }
                if (next_char == '\r') { continue; }
                if (next_char == -1) { Thread.Sleep(1); continue; };
                data += Convert.ToChar(next_char);
            }            
            return data;
        }
        public void process() {                        
            inputStream = new BufferedStream(socket.GetStream());
            try {
                parseRequest();
                readHeaders();
                
            outputStream = new StreamWriter(new BufferedStream(socket.GetStream()));
                if (http_method.Equals("GET")) {
                    handleGETRequest();
                } else if (http_method.Equals("POST")) {
                    handlePOSTRequest();
                }
            } catch (Exception e) {
                Console.WriteLine("Exception: " + e.ToString());
                writeFailure();
            }
            try
            {
                outputStream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            inputStream = null; outputStream = null;       
            socket.Close();             
        }

        public void parseRequest() {
            String request = streamReadLine(inputStream);
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3) {
                throw new Exception("invalid http request line");
            }
            http_method = tokens[0].ToUpper();
            http_url = tokens[1];
            http_protocol_versionstring = tokens[2];

            Console.WriteLine("starting: " + request);
        }

        public void readHeaders() {
            Console.WriteLine("readHeaders()");
            String line;
            while ((line = streamReadLine(inputStream)) != null) {
                if (line.Equals("")) {
                    Console.WriteLine("got headers");
                    return;
                }
                
                int separator = line.IndexOf(':');
                if (separator == -1) {
                    throw new Exception("invalid http header line: " + line);
                }
                String name = line.Substring(0, separator);
                int pos = separator + 1;
                while ((pos < line.Length) && (line[pos] == ' ')) {
                    pos++; // strip any spaces
                }
                    
                string value = line.Substring(pos, line.Length - pos);
                Console.WriteLine("header: {0}:{1}",name,value);
                if (name == "Cookie")
                {
                    string pattern = @"(([\w]+)=([\w]+);?\s*)";
                    Regex regex = new Regex(pattern);
                    Match match = regex.Match(value);
                    while (match.Success)
                    {
                        Console.WriteLine(match.Groups[2].Value + "=" + match.Groups[3].Value);
                        cookies[match.Groups[2].Value] = match.Groups[3].Value;
                        match = match.NextMatch();
                    }
                }
                httpHeaders[name] = value;
            }
        }
        public bool isAuth(string login="",string password="")
        {
            if ((login == "admin") && (password == "12345"))
            {
                return true;
            }
            else

                if ((login == "user") && (password == "qwerty"))
                {
                    return true;
                }
                else

                    if ((login == "hacker") && (password == "hacker"))
                    {
                        return true;
                    }
            return false;
        }
        public bool isAuth()
        {
            if (((string)cookies["login"] == "admin") && ((string)cookies["passw"] == "12345"))
            {
                return true;
            }
            else

                if (((string)cookies["login"] == "user") && ((string)cookies["passw"] == "qwerty"))
                {
                    return true;
                }
                else

                    if (((string)cookies["login"] == "hacker") && ((string)cookies["passw"] == "hacker"))
                    {
                        return true;
                    }
            return false;
        }
        public void printAuth()
        {
            outputStream.Write(@"<form method=post>
<input type='text' name='login'><br><input type='password' name='password'>
<input type='submit' name='submit' value='вход'></form>");
        }
        public void handleGETRequest() {
            srv.handleGETRequest(this);
        }

        private const int BUF_SIZE = 4096;
        public void handlePOSTRequest() {
            Console.WriteLine("get post data start");
            int content_len = 0;
            MemoryStream ms = new MemoryStream();
            if (this.httpHeaders.ContainsKey("Content-Length")) {
                 content_len = Convert.ToInt32(this.httpHeaders["Content-Length"]);
                 if (content_len > MAX_POST_SIZE) {
                     throw new Exception(
                         String.Format("POST Content-Length({0}) too big for this simple server",
                           content_len));
                 }
                 byte[] buf = new byte[BUF_SIZE];              
                 int to_read = content_len;
                 while (to_read > 0) {  
                     Console.WriteLine("starting Read, to_read={0}",to_read);

                     int numread = this.inputStream.Read(buf, 0, Math.Min(BUF_SIZE, to_read));
                     Console.WriteLine("read finished, numread={0}", numread);
                     if (numread == 0) {
                         if (to_read == 0) {
                             break;
                         } else {
                             throw new Exception("client disconnected during post");
                         }
                     }
                     to_read -= numread;
                     ms.Write(buf, 0, numread);
                 }
                 ms.Seek(0, SeekOrigin.Begin);
            }
            Console.WriteLine("get post data end");
            srv.handlePOSTRequest(this, new StreamReader(ms));

        }

        public void writeSuccess() {
            outputStream.Write("HTTP/1.0 200 OK\n");
            outputStream.Write("Content-Type: text/html\n");
            outputStream.Write("Connection: close\n");
            outputStream.Write("\n");
        }
        public void writeSuccessRar(string filename, long len,string type)
        {
            outputStream.Write("HTTP/1.0 200 OK\n");
            outputStream.Write(@"Content-Type: "+type+"\n");
            outputStream.Write("Accept-Ranges: bytes\n");
            outputStream.Write("Content-Length: " + len + "\n");
            outputStream.Write("Content-disposition: attachment; filename=" + filename + "\n");
            outputStream.Write("Connection: close\n");
            outputStream.Write("\n");
        }

        public void writeFailure() {
            outputStream.Write("HTTP/1.0 404 File not found\n");
            outputStream.Write("Connection: close\n");
            outputStream.Write("\n");
        }

        public bool setAuth(string data)
        {

            outputStream.Write("HTTP/1.0 200 OK\n");
            outputStream.Write("Content-Type: text/html\n");
            string pattern = @"login=(.*?)&";
            Regex regex = new Regex(pattern);
            Match match = regex.Match(data);
            string login="", password="";
            if (match.Success)
            {
                login = match.Groups[1].Value;
                Console.WriteLine("LOGIN=" + login);
                pattern = @"password=(.*?)&";
                regex = new Regex(pattern);
                match = regex.Match(data);
                if (match.Success)
                {
                    password = match.Groups[1].Value;
                    Console.WriteLine("PASSWROD=" + password);
                    if (isAuth(login,password))
                    {
                        cookies["login"] = login;
                        cookies["passw"] = password;
                        outputStream.Write("Set-Cookie: login="+login+";\n");
                         outputStream.Write("Set-Cookie: passw="+password+";\n");
                        outputStream.Write("Connection: close\n");
                        outputStream.Write("\n");
                        return true;
                    }
                }
            }
            outputStream.Write("Connection: close\n");
            outputStream.Write("\n");
            return false;
        }
    }

    public abstract class HttpServer {

        protected int port;
        TcpListener listener;
        bool is_active = true;

        protected static Process process_1, process_2, process_3, process_4;//По одному на каждую домашку
        public HttpServer(int port) {
            this.port = port;
        }

        public void listen() {
            listener = new TcpListener(port);
            listener.Start();
            while (is_active) {                
                TcpClient s = listener.AcceptTcpClient();
                HttpProcessor processor = new HttpProcessor(s, this);
                Thread thread = new Thread(new ThreadStart(processor.process));
                thread.Start();
                Thread.Sleep(1);
            }
        }

        public abstract void handleGETRequest(HttpProcessor p);
        public abstract void handlePOSTRequest(HttpProcessor p, StreamReader inputData);
    }

    public class MyHttpServer : HttpServer {
        public MyHttpServer(int port)
            : base(port) {
        }
        public static Process proc = new Process();
        public override void handleGETRequest(HttpProcessor p) {
            if (p.http_url == "/")
                p.http_url = "/index.html";
            Console.WriteLine("request: {0}", p.http_url);
            Console.WriteLine(Directory.GetCurrentDirectory());
            int nBytes = 2048;
            FileStream fs;
            try
            {
                 if (p.http_url.Split('/')[1] == "Files")
                 {
                     fs = new FileStream(Directory.GetCurrentDirectory() + p.http_url, FileMode.Open, FileAccess.Read);
                     p.writeSuccessRar("", fs.Length, @"application/x-rar-compressed, application/octet-stream");
                     while (true)
                     {
                         byte[] ByteArray = new byte[nBytes];
                         int nBytesRead = fs.Read(ByteArray, 0, nBytes);
                         if (nBytesRead == 0)
                             break;
                         p.outputStream.BaseStream.Write(ByteArray, 0, ByteArray.Length);
                     }

                     fs.Close();
                 }
                 else
                 {
                     p.writeSuccess();
                     p.outputStream.Write(@"<!DOCTYPE HTML PUBLIC '-//W3C//DTD HTML 4.0 Transitional//EN'>
<html><meta charset='utf-8'><head><title></title></head>
");
                     if (!p.isAuth())
                     {
                         p.printAuth();
                         return;
                     }
p.outputStream.Write(@"<body>");
PrintTable(p);
                 }
            }
            catch (Exception ex)
            {
                p.outputStream.WriteLine("404 - файл " + p.http_url + " не найден.<br>" + ex.Message);
                Console.WriteLine("404 - файл " + p.http_url + " не найден.<br>" + ex.Message);
            }

        }
        private static void PrintTable(HttpProcessor p)
        {
            p.outputStream.WriteLine(@"	<table border='1' width='100%'>
    <tr><td>Задание </td>
        <td>Скачать клиент</td><td>Запустить сервер</td></tr> <tr><td>1 </td><td>
            <a href='/Files/ClientConsole.rar' target='_blank'  >скачать</a></td><td>");
                     if (process_1 == null)
                     {
                         p.outputStream.Write(@"
    <form method=post action=/>	<input type='hidden' name='id_proc' value='1START'>
            <input type='submit' name='download_1' value='Запустить'></form>");
                     }
                     else
                     {
                         p.outputStream.Write(@"
    <form method=post action=/>	<input type='hidden' name='id_proc' value='1END'>
            <input type='submit' name='download_1' value='Остановить'></form>");
                     }
                     p.outputStream.Write(@"</td></tr>");
                     p.outputStream.Write(@"<tr><td>2</td><td>
            <a href='/Files/ClientBox.rar' target='_blank'  >скачать</a></td><td>");
                     if (process_2 == null)
                     {
                         p.outputStream.Write(@"
    <form method=post action=/><input type='hidden' name='id_proc' value='2START'>
            <input type='submit' name='download_1' value='Запустить'></form>");
                     }
                     else
                     {
                         p.outputStream.Write(@"
    <form method=post action=/><input type='hidden' name='id_proc' value='2END'>
            <input type='submit' name='download_1' value='Остановить'></form>");
                     }
                     p.outputStream.Write(@"</td></tr><tr><td>3</td><td>
            <a href='/Files/FormClient.rar' target='_blank'  >скачать</a> </td> <td>");

                     if (process_3 == null)
                     {
                         p.outputStream.Write(@"
    <form method=post action=/>	<input type='hidden' name='id_proc' value='3START'>
            <input type='submit' name='download_3' value='Запустить'></form>");
                     }
                     else
                     {
                         p.outputStream.Write(@"
    <form method=post action=/>	<input type='hidden' name='id_proc' value='3END'>
            <input type='submit' name='download_1' value='Остановить'></form>");
                     }
                     p.outputStream.Write(@" </td> </tr>");
                     p.outputStream.Write(@"</tr><tr><td> 4 </td>   <td>
            <a href='/Files/SponsorClient.rar' target='_blank'  >скачать</a>    </td><td>
");
                     if (process_4 == null)
                     {
                         p.outputStream.Write(@"
    <form method=post action=/>	<input type='hidden' name='id_proc' value='4START'>
            <input type='submit' name='download_1' value='Запустить'></form>");
                     }
                     else
                     {
                         p.outputStream.Write(@"
    <form method=post action=/>	<input type='hidden' name='id_proc' value='4END'>
            <input type='submit' name='download_1' value='Остановить'></form>
");
                     }
                     p.outputStream.Write(@" </td>  </tr>");
                     p.outputStream.Write(@"</table></body></html>");
        }

        public override void handlePOSTRequest(HttpProcessor p, StreamReader inputData) {
            Console.WriteLine("POST request: {0}", p.http_url);
            string data = inputData.ReadToEnd();
            if (!p.isAuth())
            {
                if (!p.setAuth(data))
                {
                    p.outputStream.Write(@"<!DOCTYPE HTML PUBLIC '-//W3C//DTD HTML 4.0 Transitional//EN'>
<html><meta charset='utf-8'><head><title></title>
<h3>Неправильные данные</h3>");
                    p.printAuth();
                }
                else
                {
                    p.outputStream.Write(@"<!DOCTYPE HTML PUBLIC '-//W3C//DTD HTML 4.0 Transitional//EN'>
<html><meta charset='utf-8'><head><title></title>");
                }
                if (!p.isAuth()){ return;}
            }
            else
            {
                p.outputStream.Write(@"<!DOCTYPE HTML PUBLIC '-//W3C//DTD HTML 4.0 Transitional//EN'>
<html><meta charset='utf-8'><head>title></title>");
            }
            string proc_id = data.Split('&')[0];
            proc_id = proc_id.Split('=')[1];
            switch (proc_id)
            {

                case "1START":
                    try
                    {
                        if (process_1 == null)
                        {
                            
                            process_1 = new Process();
                            process_1.StartInfo.FileName = Directory.GetCurrentDirectory() + @"\Programms\One\Volovich_1_lab_server.exe";
                            process_1.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory() + @"\Programms\One";
                            process_1.Start();
                            p.outputStream.WriteLine("<script>alert('Сервер №1 запущен');</script>");
                        }
                         }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                            p.outputStream.WriteLine("exception: {0}", ex.Message);
                        }
                    break;
                case "1END":
                        process_1.Kill();
                        process_1 = null;
                    break;
                case "2START":
                    try
                    {
                        if (process_2 == null)
                        {
                            process_2 = new Process();
                            process_2.StartInfo.FileName = Directory.GetCurrentDirectory() + @"\Programms\Two\ServerBox.exe";
                            process_2.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory() + @"\Programms\Three";
                            process_2.Start();
                            p.outputStream.WriteLine("<script> alert('Сервер №2 запущен'); </script>");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        p.outputStream.WriteLine("exception: {0}", ex.Message);
                    }
                    break;
                case "2END":
                    process_2.Kill();
                    process_2 = null;
                    break;

                case "3START":
                    try
                    {
                        if (process_3 == null)
                        {
                            process_3 = new Process();
                            process_3.StartInfo.FileName = Directory.GetCurrentDirectory() + @"\Programms\Three\MathServer.exe";
                            process_3.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory() + @"\Programms\Three";
                            process_3.Start();
                            p.outputStream.WriteLine("<script> alert('Сервер №3 запущен'); </script>");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        p.outputStream.WriteLine("exception: {0}", ex.Message);
                    }
                    break;
                case "3END":
                        process_3.Kill();
                        process_3 = null;
                    break;


                case "4START":
                    try
                    {
                        if (process_4 == null)
                        {
                            process_4 = new Process();
                            process_4.StartInfo.FileName = Directory.GetCurrentDirectory() + @"\Programms\Four\SponsorServer.exe";
                            process_4.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory() + @"\Programms\Four";
                            process_4.Start();
                            p.outputStream.WriteLine("<script> alert('Сервер №4 запущен'); </script>");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        p.outputStream.WriteLine("exception: {0}", ex.Message);
                    }
                    break;
                case "4END":
                    process_4.Kill();
                    process_4 = null;
                    break;
                default:
                    break;
            }
            p.outputStream.Write(@"</head><body>");
            PrintTable(p);
          p.outputStream.Write(@"</body></html>");
           

        }
        private void myProcess_Exited(object sender, System.EventArgs e)
        {
            Console.WriteLine("Exit time:    {0}\r\n" +
                "Exit code:    {1}\r\n", process_1.ExitTime, process_1.ExitCode);
            process_1 = null;
        }
    }

    public class TestMain {
        public static int Main(String[] args) {
            HttpServer httpServer;
            if (args.GetLength(0) > 0) {
                httpServer = new MyHttpServer(Convert.ToInt16(args[0]));
            } else {
                httpServer = new MyHttpServer(8080);
            }
            Thread thread = new Thread(new ThreadStart(httpServer.listen));
            thread.Start();
            return 0;
        }
    }
}



