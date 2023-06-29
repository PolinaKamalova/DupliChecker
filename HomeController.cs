using Microsoft.AspNetCore.Mvc;
using DupliChecker.Models;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text;

namespace DupliChecker.Controllers
{
    public class HomeController : Controller
    {
        private string connectionString = "Data Source=duplicates.db;Version=3;";

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult CheckDuplicates(List<PdfFileModel> files)
        {
            List<string> pdfFilePaths = new List<string>();

            foreach (var file in files)
            {
                if (!string.IsNullOrEmpty(file.PdfFilePath))
                {
                    pdfFilePaths.Add(file.PdfFilePath);
                }
            }


            InsertValuesIntoDatabase(pdfFilePaths);

            List<string> duplicates = ListTheDuplicates();

            return View(duplicates);
        }


        private void InsertValuesIntoDatabase(List<string> pdfFilePaths)
        {
            using (SQLiteConnection connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string createTableQuery = "CREATE TABLE IF NOT EXISTS scifiles (id INTEGER, link TEXT, content TEXT, references TEXT, structure TEXT)";
                using (SQLiteCommand command = new SQLiteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }

                foreach (string pdfFilePath in pdfFilePaths)
                {
                    int content = ExtractContentFromPdf(pdfFilePath);
                    string references = ExtractReferencesFromPdf(pdfFilePath);
                    string structure = FindDocumentStructure(pdfFilePath);

                    string insertQuery = "INSERT INTO scifiles (content, references, structure) VALUES (@content, @references, @structure)";

                    using (SQLiteCommand command = new SQLiteCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@content", content);
                        command.Parameters.AddWithValue("@references", references);
                        command.Parameters.AddWithValue("@structure", structure);

                        command.ExecuteNonQuery();
                    }
                }

                connection.Close();
            }
        }

        private List<string> ListTheDuplicates()
        {
            List<string> duplicates = new List<string>();

            using (SQLiteConnection connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT link, COUNT(*) as count " +
                               "FROM scifiles " +
                               "GROUP BY link " +
                               "HAVING count > 1";

                using (SQLiteCommand command = new SQLiteCommand(query, connection))
                {
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string link = reader["link"].ToString();
                            int count1 = reader.GetInt32("count");

                            duplicates.Add($"Найдено {count1} дубликатов работы по ссылке {link}");
                        }
                    }
                }

                connection.Close();
            }

            return duplicates;
        }

        private int ExtractContentFromPdf(string pdfFilePath)
        {
            using (PdfReader reader = new PdfReader(pdfFilePath))
            {
                int content = 0;

                for (int i = 1; i <= reader.NumberOfPages; i++)
                {
                    SimpleTextExtractionStrategy strategy = new SimpleTextExtractionStrategy();
                    string currentPageText = PdfTextExtractor.GetTextFromPage(reader, i, strategy);
                    if (currentPageText.Contains("Содержание"))
                    {
                        int startIndex = currentPageText.IndexOf("Содержание") + "Содержание".Length;
                        int lineCount = 0;
                        string[] lines = currentPageText.Split('\n');
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrEmpty(line.Trim()))
                            {
                                lineCount++;
                            }
                        }

                        return lineCount;
                    }
                }
            }
            return 0;
        }

        private string ExtractReferencesFromPdf(string pdfFilePath)
        {
            using (PdfReader reader = new PdfReader(pdfFilePath))
            {
                string references = string.Empty;
                int startPage = 1;
                int endPage = reader.NumberOfPages;

                for (int i = startPage; i <= endPage; i++)
                {
                    SimpleTextExtractionStrategy strategy = new SimpleTextExtractionStrategy();
                    string currentPageText = PdfTextExtractor.GetTextFromPage(reader, i, strategy);

                    if (currentPageText.Contains("Список литературы"))
                    {
                        int startIndex = currentPageText.IndexOf("Список литературы") + "Список литературы".Length;
                        string referencesText = currentPageText.Substring(startIndex);
                        references += referencesText;
                    }
                }

                return references;
            }
            return string.Empty;
        }

        private string FindDocumentStructure(string pdfFilePath)
        {
            PdfReader reader = new PdfReader("input.pdf");

            List<string> formulaList = new List<string>();
            List<byte[]> imageList = new List<byte[]>();
            string hashLine = "";
            byte[] hashBytes = new byte[1000000];
            string structure = "";
            for (int pageNum = 1; pageNum <= reader.NumberOfPages; pageNum++)
            {
                string pageContent = PdfTextExtractor.GetTextFromPage(reader, pageNum);

                string[] lines = pageContent.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    if (line.Contains("$"))
                    {
                        formulaList.Add(line);
                        structure += "f" + Line;
                    }
                    else
                    {
                        PdfObject obj = PdfReader.GetPdfObject(reader.GetPageN(pageNum).Get(PdfName.CONTENTS));
                        if (obj != null && obj.IsStream())
                        {
                            PRStream stream = (PRStream)obj;
                            if (stream.Contains(PdfName.XOBJECT))
                            {
                                PdfDictionary resources = (PdfDictionary)PdfReader.GetPdfObject(reader.GetPageN(pageNum).Get(PdfName.RESOURCES));
                                PdfDictionary xobjects = (PdfDictionary)PdfReader.GetPdfObject(resources.Get(PdfName.XOBJECT));
                                foreach (PdfName name in xobjects.Keys)
                                {
                                    PdfObject o = xobjects.Get(name);
                                    if (o.IsIndirect())
                                    {
                                        PdfDictionary xo = (PdfDictionary)PdfReader.GetPdfObject(o);
                                        if (xo.Contains(PdfName.SUBTYPE) && xo.Get(PdfName.SUBTYPE).Equals(PdfName.IMAGE))
                                        {
                                            byte[] bytes = PdfReader.GetStreamBytes(stream);
                                            imageList.Add(bytes);
                                            using (SHA256 sha256 = SHA256.Create())
                                            {
                                                hashBytes = sha256.ComputeHash(bytes);
                                            }
                                            string i = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

                                            structure += "i" + i;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            reader.Close();
            return structure;
        }
    }
}
