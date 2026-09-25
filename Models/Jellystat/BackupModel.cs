namespace Jellystat_API_Core.Models.Jellystat
{
    public class BackupModel
    {
        public string FileName { get; set; }
        public DateTime Date { get; set; }
        public long Size { get; set; }

        public BackupModel(string fileName, DateTime date, long size)
        {
            FileName = fileName;
            Date = date;
            Size = size;
        }
    }
}
