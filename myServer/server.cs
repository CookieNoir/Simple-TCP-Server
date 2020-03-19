using System;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using MongoDB.Bson;
using MongoDB.Driver;

namespace myServer
{
    public class Entity
    {
        public ObjectId _id;
        public string content;
    }

    class Server
    {

        static int DefineRequest(string data, ref int urlIndex)
        {
            int i = 0;
            while (data[i] != ' ') i++;
            string type = data.Substring(0, i);
            urlIndex = i + 1;
            if (type == "GET") return 1;
            else if (type == "POST") return 2;
            else return 0;
        }

        static string GetUrl(string data, int index)
        {
            int i = index;
            while (data[i] != ' ' && data[i] != '\r' && data[i] != '\n') i++;
            return data.Substring(index, i - index);
        }

        static string GetBody(string data)
        {
            int index = data.IndexOf("\r\n\r\n");
            return data.Substring(index + ("\r\n\r\n").Length);
        }

        static void MakeJson(ref string body)
        {
            DataContractJsonSerializer js = new DataContractJsonSerializer(typeof(string));
            using (MemoryStream msObj = new MemoryStream())
            {
                js.WriteObject(msObj, body);
                msObj.Position = 0;
                using (StreamReader sr = new StreamReader(msObj))
                {
                    body = sr.ReadToEnd();
                }
            }
        }

        static bool IsJson(string data)
        {
            data = data.Trim();
            return data.StartsWith("{") && data.EndsWith("}")
                || data.StartsWith("[") && data.EndsWith("]");
        }

        static byte[] GetResponse(string codeAndDescription, string contentType, byte[] body)
        {
            string responseHeaders =
                "HTTP/1.1 " + codeAndDescription + "\r\n" +
                "Host: localhost:80\r\n" +
                "Connection: keep-alive\r\n" +
                "Upgrade-Insecure-Requests: 1\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                "\r\n";
            byte[] headers = Encoding.UTF8.GetBytes(responseHeaders);
            byte[] response = new byte[headers.Length + body.Length];
            System.Buffer.BlockCopy(headers, 0, response, 0, headers.Length);
            System.Buffer.BlockCopy(body, 0, response, headers.Length, body.Length);
            return response;
        }

        static byte[] GetContent(IMongoCollection<Entity> collection, string url)
        {
            int index = url.IndexOf("/**GetObject**?id=");
            string code = null, contentType = null;
            byte[] body;
            if (index != -1)
            {
                try
                {
                    string objectid = url.Substring(index + ("/**GetObject**?id=").Length);
                    Console.WriteLine("Looking for the object with ID: " + objectid);
                    string entitiesString = null;
                    List<Entity> entities = collection.Find(new BsonDocument()).ToList();
                    foreach (var entity in entities)
                    {
                        entitiesString += entity.content; // Здесь должно быть что-то еще
                    }
                    code = "200 OK";
                    contentType = "application/json";
                    body = Encoding.UTF8.GetBytes(entitiesString);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    code = "404 Not Found";
                    contentType = "text/html";
                    body = Encoding.UTF8.GetBytes("Can't get an object by ID.");
                }
            }
            else
            {
                string path = Directory.GetCurrentDirectory() + url.Replace('/', '\\');
                if (File.Exists(path))
                {
                    code = "200 OK";
                    string extension = Path.GetExtension(path).Substring(1);
                    if (extension == "ico" || extension == "jpg" || extension == "png" || extension == "bmp")
                        contentType = "image/" + extension;
                    else
                        contentType = "text/html; charset=utf-8";
                    body = File.ReadAllBytes(path);
                }
                else
                {
                    code = "404 Not Found";
                    contentType = "text/html";
                    body = Encoding.UTF8.GetBytes("Incorrect file path: " + path);
                }
            }
            return GetResponse(code, contentType, body);
        }

        static void AddContent(IMongoCollection<Entity> collection, string data)
        {
            string body = GetBody(data);
            if (!IsJson(body))
            {
                MakeJson(ref body);
            }
            collection.InsertOne(new Entity { content = body });
        }

        static void Main(string[] args)
        {
            string connectionString =
                "mongodb://nchernyshov:R5WMPtrz@caiiik.com:27017/?authMechanism=SCRAM-SHA-1&authSource=server_database"
            ;
            MongoClient client = new MongoClient(connectionString);
            IMongoDatabase database = client.GetDatabase("server_database");
            try
            {
                database.RunCommandAsync((Command<BsonDocument>)"{ping:1}")
               .Wait(1000);

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.WriteLine("Can't connect to MongoDB. Some requests may be processed incorrectly.");
            }
            IMongoCollection<Entity> collection = database.GetCollection<Entity>("entities");

            IPHostEntry ipHost = Dns.GetHostEntry("localhost");
            IPAddress ipAddr = ipHost.AddressList[0];
            IPEndPoint ipEndPoint = new IPEndPoint(ipAddr, 80);

            Socket sListener = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                sListener.Bind(ipEndPoint);
                sListener.Listen(10);

                while (true)
                {
                    Console.WriteLine("Waiting for a connection through the port " + ipEndPoint);

                    Socket handler = sListener.Accept();
                    string data = null;
                    byte[] bytes = new byte[1024];
                    while (true)
                    {
                        bytes[1023] = 0;
                        int bytesRec = handler.Receive(bytes);

                        data += Encoding.UTF8.GetString(bytes, 0, bytesRec);
                        if (bytes[1023] == 0) break;
                    }
                    if (data.Length == 0) continue;
                    Console.Write("Received text is: \n" + data + "\n\n");

                    int urlIndex = 0;
                    int requestType = DefineRequest(data, ref urlIndex);
                    string url = GetUrl(data, urlIndex);

                    byte[] msg = null;
                    switch (requestType)
                    {
                        case 1: //GET
                            {
                                msg = GetContent(collection, url);
                                break;
                            }
                        case 2: //POST
                            {
                                AddContent(collection, data);
                                msg = GetResponse("200 OK", "text/html", Encoding.UTF8.GetBytes("Body of POST request is saved into MongoDB"));
                                break;
                            }
                        default: //Something else
                            {
                                msg = GetResponse("400 Bad Request", "text/html", Encoding.UTF8.GetBytes("Incorrect request"));
                                break;
                            }
                    }
                    handler.Send(msg);

                    if (data.IndexOf("<TheEnd>") > -1)
                    {
                        Console.WriteLine("The server has ended the connection with the client.");
                        break;
                    }

                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                Console.ReadLine();
            }
        }
    }
}