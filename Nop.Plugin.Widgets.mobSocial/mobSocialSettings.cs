﻿
using Nop.Core.Configuration;

namespace Nop.Plugin.Widgets.MobSocial
{
    public class mobSocialSettings : ISettings
    {
        public int ProfilePictureSize { get; set; }
        public string WidgetZone { get; set; }

        public bool ShowProfileImagesInSearchAutoComplete { get; set; }

        public int PeopleSearchTermMinimumLength { get; set; }

        public int PeopleSearchAutoCompleteNumberOfResults { get; set; }

        public bool PeopleSearchAutoCompleteEnabled { get; set; }

        public int CustomerAlbumPictureThumbnailWidth { get; set; }

        public int MaximumMainAlbumPictures { get; set; }

        public int MaximumMainAlbumVideos { get; set; }

        public int EventPageSearchTermMinimumLength { get; set; }

        public int EventPageSearchAutoCompleteNumberOfResults { get; set; }
    }
}