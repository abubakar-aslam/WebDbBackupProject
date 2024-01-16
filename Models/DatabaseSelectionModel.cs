using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace BackupDBWeb.Models
{
    public class DatabaseSelectionModel
    {
        public string DatabaseNames { get; set; }
        public bool IsDatabaseSelected { get; set; }
    }
}
