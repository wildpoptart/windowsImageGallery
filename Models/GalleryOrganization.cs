using System;
using System.Collections.Generic;

namespace PhotoOrganizer.Models
{
    public enum OrganizationType
    {
        None,
        ByDate
    }

    public class GalleryGroup
    {
        public string Header { get; set; }
        public List<ImageThumbnail> Items { get; set; } = new List<ImageThumbnail>();
        public List<GalleryGroup> SubGroups { get; set; } = new List<GalleryGroup>();
    }
} 