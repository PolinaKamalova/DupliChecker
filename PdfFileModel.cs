using System.ComponentModel.DataAnnotations;

namespace DuplicateCheckerApp.Models
{
    public class PdfFileModel
    {
        [Required(ErrorMessage = "Путь к PDF-файлу обязателен.")]
        [Display(Name = "PDF-файл")]
        public string PdfFilePath { get; set; }
    }
}
