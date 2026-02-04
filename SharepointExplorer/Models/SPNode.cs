using System;
using System.Collections.ObjectModel;

namespace SharepointExplorer.Models
{
    public class SPNode
    {
        public string Name { get; set; } = string.Empty;
        public string ServerRelativeUrl { get; set; } = string.Empty;
        public SPItemType Type { get; set; }
        public DateTime? LastModified { get; set; }
        public long Size { get; set; }
        public string Author { get; set; } = string.Empty;
        
        public string TypeDisplay => Type.ToString();

        // For TreeView Hierarchy
        public ObservableCollection<SPNode> Children { get; set; } = new ObservableCollection<SPNode>();
        
        // Very basic lazy loading indicator (not fully implemented in this demo)
        public bool IsExpanded { get; set; }
    }
}