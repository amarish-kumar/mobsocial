﻿using Nop.Web.Controllers;
using Nop.Plugin.Widgets.MobSocial.Core;
using Nop.Web.Framework.Security;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using System.Collections.Generic;
using Nop.Plugin.Widgets.MobSocial.Models;
using Nop.Plugin.Widgets.MobSocial.Domain;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web;
using Mob.Core;
using Nop.Plugin.Widgets.MobSocial.Helpers;
using Nop.Core.Infrastructure;

namespace Nop.Plugin.Widgets.MobSocial.Controllers
{
    [NopHttpsRequirement(SslRequirement.No)]
    public partial class SongController : BasePublicController
    {
        #region variables
        private readonly ILocalizationService _localizationService;
        private readonly IPictureService _pictureService;
        private readonly ICustomerService _customerService;
        private readonly IDateTimeHelper _dateTimeHelper;
        private readonly CustomerSettings _customerSettings;
        private readonly MediaSettings _mediaSettings;
        private readonly mobSocialSettings _mobSocialSettings;
        private readonly IWorkContext _workContext;
        private readonly IMobSocialService _mobSocialService;
        private readonly IArtistPageService _artistPageService;
        private readonly IArtistPageAPIService _artistPageApiService;
        private readonly ISongService _songService;
        private readonly IMusicService _musicService;
        private readonly IMobSocialMessageService _mobsocialMessageService;
        private readonly ISharedSongService _sharedSongService;
        private readonly IStoreContext _storeContext;

        public SongController(ILocalizationService localizationService,
            IPictureService pictureService,
            ICustomerService customerService,
            IDateTimeHelper dateTimeHelper,
            CustomerSettings customerSettings,
            MediaSettings mediaSettings,
            IArtistPageService artistPageService,
            IArtistPageAPIService artistPageApiService,
            ISongService songService,
            IMusicService musicService,
            mobSocialSettings mobSocialSettings,
            IMobSocialService mobSocialService,
            IWorkContext workContext,
            IMobSocialMessageService mobsocialMessageService,
            ISharedSongService sharedSongService,
            IStoreContext storeContext)
        {
            _localizationService = localizationService;
            _pictureService = pictureService;
            _customerService = customerService;
            _dateTimeHelper = dateTimeHelper;
            _customerSettings = customerSettings;
            _mediaSettings = mediaSettings;
            _mobSocialSettings = mobSocialSettings;
            _mobSocialService = mobSocialService;
            _workContext = workContext;
            _artistPageApiService = artistPageApiService;
            _artistPageService = artistPageService;
            _songService = songService;
            _musicService = musicService;
            _mobsocialMessageService = mobsocialMessageService;
            _sharedSongService = sharedSongService;
            _storeContext = storeContext;
        }

        #endregion

        #region Actions
        public ActionResult Index(int Id)
        {
            var song = _songService.GetById(Id);

            if (song == null)
                return InvokeHttp404(); //not found
            string affiliateUrl = "";
            int trackId;
            if (int.TryParse(song.TrackId, out trackId))
                affiliateUrl = _musicService.GetTrackAffiliateUrl(trackId);
            var model = new SongModel() {
                Description = song.Description,               
                Name = song.Name,
                RemoteEntityId = song.RemoteEntityId,
                RemoteSourceName = song.RemoteSourceName,                
                TrackId = song.TrackId,
                Id = song.Id,
                AffiliateUrl = affiliateUrl
                
            };

            //images for artist
            foreach (var picture in song.Pictures)
            {
                model.Pictures.Add(new PictureModel {
                    Id = picture.Id,
                    EntityId = song.Id,
                    PictureId = picture.PictureId,
                    DisplayOrder = picture.DisplayOrder,
                    DateCreated = picture.DateCreated,
                    DateUpdated = picture.DateUpdated,
                    PictureUrl = _pictureService.GetPictureUrl(picture.PictureId, 0, true),
                });
            }
            if (model.Pictures.Count > 0)
                model.MainPictureUrl = model.Pictures[0].PictureUrl;

            model.CanEdit = CanEdit(song);
            model.CanDelete = CanDelete(song);

            return View(ControllerUtil.MobSocialViewsFolder + "/SongPage/Index.cshtml", model);
        }


        /// <summary>
        /// Imports a new song from remote api if it doesn't exist in our database
        /// </summary>
        public ActionResult RemoteSong(string RemoteEntityId)
        {
            if (!string.IsNullOrEmpty(RemoteEntityId))
            {
                var song = _songService.GetSongByRemoteEntityId(RemoteEntityId);
                if (song == null)
                {
                    //we need to create a new song now
                    var remoteSong = _artistPageApiService.GetRemoteSong(RemoteEntityId);
                    if (remoteSong == null)
                        return InvokeHttp404();

                    song = SaveRemoteSongToDB(remoteSong);
                    return RedirectToRoute("SongUrl", new { SeName = song.GetSeName(_workContext.WorkingLanguage.Id, true, false) });
                }
                else
                {
                    //the page already exists in our database. No need to create duplicate entries. Rather redirect them to the actual artist page
                    return RedirectToRoute("SongUrl", new { SeName = song.GetSeName(_workContext.WorkingLanguage.Id, true, false) });
                }
            }
            //totally unknown path
            return InvokeHttp404();
        }

        
        [HttpPost]
        public ActionResult GetSongPreviewUrl(string TrackId)
        {
            int iTrackId;
            if (int.TryParse(TrackId, out iTrackId))
            {
                var previewUrl = _musicService.GetTrackPreviewUrl(iTrackId);
                return Json(new { Success = true, PreviewUrl = previewUrl });
            }
            return Json(new { Success = false, Message = "Invalid Track Id" });
        }

        [HttpPost]
        public ActionResult Search(string Term, int Count = 15, int Page = 1, bool SearchDescriptions = false, bool SearchArtists = false, string ArtistName = "")
        {
            //we search for artists both in our database as well as the remote api
            var model = new List<object>();

            //first let's search our database
            var dbSongs = _songService.SearchSongs(Term, Count, Page, SearchDescriptions, SearchArtists);

            //first add db artists
            foreach (var dbs in dbSongs)
            {
                var imageUrl = "";
                if (dbs.Pictures.Count > 0)
                    imageUrl = _pictureService.GetPictureUrl(dbs.Pictures.First().PictureId, _mobSocialSettings.ArtistPageThumbnailSize, true);
                else
                    imageUrl = _pictureService.GetPictureUrl(0, _mobSocialSettings.ArtistPageThumbnailSize, true);

                string affiliateUrl = "";
                int iTrackId;
                if (int.TryParse(dbs.TrackId, out iTrackId))
                {
                    affiliateUrl = _musicService.GetTrackAffiliateUrl(iTrackId);
                }

             
                model.Add(new {
                    Name = dbs.Name,
                    Id = dbs.Id,
                    ImageUrl = imageUrl,
                    SeName = dbs.GetSeName(_workContext.WorkingLanguage.Id, true, false),
                    TrackId = dbs.TrackId,
                    AffiliateUrl = affiliateUrl,
                    RemoteSong = false
                });
            }

            //do we need more records to show?
            if (dbSongs.Count() < Count)
            {
                //we need more records to show. lets go remote and import some records from there
                var remoteSongs = _artistPageApiService.SearchSongs(Term, SearchArtists ? ArtistName : string.Empty /*artist name*/, Count - dbSongs.Count());
                if (remoteSongs != null)
                {
                    var remoteSongsDeserialized = new List<JObject>();
                    foreach (string rsItem in remoteSongs)
                    {
                        remoteSongsDeserialized.Add((JObject)JsonConvert.DeserializeObject(rsItem));
                    }

                    var remoteSongIds = remoteSongsDeserialized.Select(x => x["RemoteEntityId"].ToString()).ToList();

                    //filter out the results which are already in our result set
                    remoteSongIds = remoteSongIds.Except(dbSongs.Select(x => x.RemoteEntityId)).ToList();

                    //now add remote artists if any
                    foreach (string raid in remoteSongIds)
                    {
                        var songJson = remoteSongsDeserialized.Where(x => x["RemoteEntityId"].ToString() == raid).First().ToString();
                        var song = (JObject)JsonConvert.DeserializeObject(songJson);
                        string affiliateUrl = "";
                        string previewUrl = "";
                        int iTrackId;
                        if (int.TryParse(song["TrackId"].ToString(), out iTrackId))
                        {
                            affiliateUrl = _musicService.GetTrackAffiliateUrl(iTrackId);
                            previewUrl = _musicService.GetTrackPreviewUrl(iTrackId);
                        }

                        model.Add(new {
                            Name = song["Name"].ToString(),
                            Id = raid,
                            ImageUrl = song["ImageUrl"].ToString(),
                            SeName = raid,
                            TrackId = song["TrackId"].ToString(),
                            AffiliateUrl = affiliateUrl,
                            PreviewUrl = previewUrl,
                            RemoteSong = true
                        });

                    }
                }

            }
            return Json(model);
        }

        /// <summary>
        /// Gets the similar songs to a given song
        /// </summary>
        [HttpPost]
        public ActionResult GetSimilarSongs(string RemoteTrackId, int Count = 5)
        {
            
            var model = new List<object>();

            var remoteSongs = _artistPageApiService.GetSimilarSongs(RemoteTrackId, Count);
            if (remoteSongs == null)
            return Json(model);

            foreach (var songJson in remoteSongs)
            {
                var song = (JObject)JsonConvert.DeserializeObject(songJson);
                string affiliateUrl = "";
                int iTrackId;
                if (int.TryParse(song["TrackId"].ToString(), out iTrackId))
                {
                    affiliateUrl = _musicService.GetTrackAffiliateUrl(iTrackId);
        }


                model.Add(new {
                    Name = song["Name"].ToString(),
                    Id = song["RemoteEntityId"].ToString(),
                    ImageUrl = song["ImageUrl"].ToString(),
                    SeName =  song["RemoteEntityId"].ToString(),
                    TrackId = song["TrackId"].ToString(),
                    AffiliateUrl = affiliateUrl,
                    RemoteSong = true
                });
            }
            return Json(model);
        }
        /// <summary>
        /// Generic method for all inline updates
        /// </summary>
        [HttpPost]
        public ActionResult UpdateSongData(FormCollection Parameters)
        {
            if (!_workContext.CurrentCustomer.IsRegistered())
                return InvokeHttp404();

            var IdStr = Parameters["id"];
            int Id;
            if (int.TryParse(IdStr, out Id))
            {
                var song = _songService.GetById(Id);

                if (CanEdit(song))
                {
                    //find the key that'll be updated
                    var key = Parameters["key"];
                    var value = Parameters["value"];
                    switch (key)
                    {
                        case "Name":
                            song.Name = value;
                            break;
                        case "Description":
                            song.Description = value;
                            break;                       
                    }
                    _songService.Update(song);
                    return Json(new { success = true });
                }
                else
                {
                    return Json(new { success = false, message = "Unauthorized" });

                }
            }
            else
            {
                return Json(new { success = false, message = "Invalid artist" });
            }

        }

        public ActionResult ShareSong(int TrackId, string RemoteTrackId = "")
        {
            if (!_workContext.CurrentCustomer.IsRegistered())
            {
                //ask user to login if he is logged out
                return View(ControllerUtil.MobSocialViewsFolder + "/_MustLogin.cshtml", "/Music");
            }
            //check if song exists
            var song = _songService.GetById(TrackId);
            SongModel model;
            if (song == null)
            {
                //song is not there. let's first import the song
                //TODO: Find a way to import the song before it can be shared. The code below slows down the process. so just comment it for now
                /*
                if (!string.IsNullOrWhiteSpace(RemoteTrackId))
                {
                    //we need to create a new song now
                    var remoteSong = _artistPageApiService.GetRemoteSong(RemoteTrackId);
                    if (remoteSong == null)
                        return InvokeHttp404();

                    song = SaveRemoteSongToDB(remoteSong);
                }
                 * */
                model = new SongModel() {
                    Name = "",
                    SeName = RemoteTrackId,
                    Id = 0,
                    RemoteEntityId = RemoteTrackId,
                    RemoteSourceName = "",
      
                };
            }
            else
            {
                model = new SongModel() {
                    Name = song.Name,
                    SeName = song.GetSeName(_workContext.WorkingLanguage.Id, true, false),
                    Id = song.Id,
                    RemoteEntityId = song.RemoteEntityId,
                    RemoteSourceName = song.RemoteSourceName,

                };
            }

            return View(ControllerUtil.MobSocialViewsFolder + "/SongPage/ShareSong.cshtml", model);

        }
        [HttpPost]
        public ActionResult ShareSong(int TrackId, int[] CustomerIds, string Message = "", string RemoteTrackId = "")
        {
            if(CustomerIds == null)
                return Json(new { Success = false, Message = "Failed" });

            //check if song exists
            var song = _songService.GetById(TrackId);
            if (song == null)
            {
                //song is not there. let's first import the song
                if (!string.IsNullOrWhiteSpace(RemoteTrackId))
                {
                    //we need to create a new song now
                    var remoteSong = _artistPageApiService.GetRemoteSong(RemoteTrackId);
                    if (remoteSong == null)
                        return Json(new { Success = false, Message = "Failed" });

                    song = SaveRemoteSongToDB(remoteSong);
                }
                else
                {
                    return Json(new { Success = false, Message = "InvalidId" });
                }
            }
            if (_workContext.CurrentCustomer.IsRegistered())
            {
                foreach (var CustomerId in CustomerIds.Distinct())
                {

                    {
                        var sharedSong = new SharedSong() {
                            CustomerId = CustomerId,
                            SenderId = _workContext.CurrentCustomer.Id,
                            SongId = song.Id,
                            Message = Message,
                            SharedOn = DateTime.Now
                        };
                        _sharedSongService.Insert(sharedSong);
                        //send the notification to the customer
                        var customer = _customerService.GetCustomerById(CustomerId);
                        _mobsocialMessageService.SendSomeoneSentYouASongNotification(customer, _workContext.WorkingLanguage.Id, _storeContext.CurrentStore.Id);
                    }

                }
                return Json(new { Success = true });  
            }
            else
            {
                return Json(new { Success = false, Message = "Unauthorized" });
            }
                      
           
        }

        /// <summary>
        /// The shared songs page for user's profile
        /// </summary>
        public ActionResult SharedSongs()
        {
            return View(ControllerUtil.MobSocialViewsFolder + "/SongPage/SharedSongs.cshtml");
        }

        [HttpPost]
        public ActionResult GetReceivedSongs(int Count = 15, int Page = 1)
        {
            var smodel = new List<object>();
            int totalPages = 0;
            if (_workContext.CurrentCustomer.IsRegistered())
            {
                var receivedSongs = _sharedSongService.GetReceivedSongs(_workContext.CurrentCustomer.Id, out totalPages, Count, Page);
                foreach (var rs in receivedSongs)
                {
                    var imageUrl = "";
                    var song = rs.Song;

                    if (song.Pictures.Count > 0)
                        imageUrl = _pictureService.GetPictureUrl(song.Pictures.First().PictureId, _mobSocialSettings.ArtistPageThumbnailSize, true);
                    else
                        imageUrl = _pictureService.GetPictureUrl(0, _mobSocialSettings.ArtistPageThumbnailSize, true);

                    var sender = _customerService.GetCustomerById(rs.SenderId);

                    smodel.Add(new {
                        Name = song.Name,
                        Id = song.Id,
                        Message = rs.Message,
                        ImageUrl = imageUrl,
                        SeName = song.GetSeName(_workContext.WorkingLanguage.Id, true, false),
                        TrackId = song.TrackId,
                        AffiliateUrl = _musicService.GetTrackAffiliateUrl(int.Parse(song.TrackId)),
                        SenderName = sender.GetFullName(),
                        SenderUrl = sender.GetSeName(_workContext.WorkingLanguage.Id, true, false),
                        RemoteSong = false
                    });
                }
                
            }
            var model = new {
                Songs = smodel,
                TotalPages = totalPages
            };
            return Json(model);
        }

        [HttpPost]
        public ActionResult GetSharedSongs(int Count = 15, int Page = 1)
        {
            var smodel = new List<object>();
            int totalPages = 0;
            if (_workContext.CurrentCustomer.IsRegistered())
            {
                var receivedSongs = _sharedSongService.GetSharedSongs(_workContext.CurrentCustomer.Id, out totalPages, Count, Page);
                if (receivedSongs.Count() > 0)
                {
                    //find distinct entries because we'll be showing all 
                    var filteredSongs = receivedSongs.GroupBy(x => x.Song).ToDictionary(g => g.Key, g => g.Select(x => x.CustomerId).ToList());
                    foreach (var kv in filteredSongs)
                    {
                        var imageUrl = "";
                       
                        var sharedWith = kv.Value;

                        var customerModel = new List<object>();
                        foreach (var customerId in sharedWith)
                        {
                            var receiver = _customerService.GetCustomerById(customerId);

                            customerModel.Add(new {
                                Name = receiver.GetFullName(),
                                Url = receiver.GetSeName(_workContext.WorkingLanguage.Id, true, false)
                            });
                        }

                        var song = kv.Key;
                        if (song.Pictures.Count > 0)
                            imageUrl = _pictureService.GetPictureUrl(song.Pictures.First().PictureId, _mobSocialSettings.ArtistPageThumbnailSize, true);
                        else
                            imageUrl = _pictureService.GetPictureUrl(0, _mobSocialSettings.ArtistPageThumbnailSize, true);

                        smodel.Add(new {
                            Name = song.Name,
                            Id = song.Id,
                            ImageUrl = imageUrl,
                            SeName = song.GetSeName(_workContext.WorkingLanguage.Id, true, false),
                            TrackId = song.TrackId,
                            AffiliateUrl = _musicService.GetTrackAffiliateUrl(int.Parse(song.TrackId)),
                            RemoteSong = false,
                            SharedWith = customerModel
                        });

                    }                    
                 
                }
                

            }
            var model = new {
                Songs = smodel,
                TotalPages = totalPages
            };
            return Json(model);
        }

        [HttpPost]
        public ActionResult DeleteSong(int SongId)
        {
            var song = _songService.GetById(SongId);
            if (CanDelete(song))
            {
                //the logged in user can delete the page. lets delete the associated things now
                while (song.Pictures.Count() != 0)
                {
                    foreach (var songpicture in song.Pictures)
                    {
                        var picture = _pictureService.GetPictureById(songpicture.PictureId);
                        _songService.DeletePicture(songpicture);
                        _pictureService.DeletePicture(picture);
                        //collection modified. so better break the loop and let it run agian.

                        break;
                    }
                }

                _songService.Delete(song);
                return Json(new { Success = true });
            }
            else
            {
                return Json(new { Success = false, Message = "Unauthorized" });
            }
        }

        [HttpPost]
        public ActionResult UploadPicture(int SongId, IEnumerable<HttpPostedFileBase> file)
        {

            //first get song
            var song = _songService.GetById(SongId);
            if (!CanEdit(song))
                return Json(new { Success = false, Message = "Unauthorized" });

            var files = file.ToList();
            foreach (var fi in files)
            {
                Stream stream = null;
                var fileName = "";
                var contentType = "";

                if (file == null)
                    throw new ArgumentException("No file uploaded");

                stream = fi.InputStream;
                fileName = Path.GetFileName(fi.FileName);
                contentType = fi.ContentType;

                var fileBinary = new byte[stream.Length];
                stream.Read(fileBinary, 0, fileBinary.Length);

                var fileExtension = Path.GetExtension(fileName);
                if (!String.IsNullOrEmpty(fileExtension))
                    fileExtension = fileExtension.ToLowerInvariant();


                if (String.IsNullOrEmpty(contentType))
                {
                    contentType = PictureUtility.GetContentType(fileExtension);
                }

                var picture = _pictureService.InsertPicture(fileBinary, contentType, null, true);


                var firstSongPicture = _songService.GetFirstEntityPicture(SongId);

                if (firstSongPicture == null)
                {
                    firstSongPicture = new SongPicture() {
                        SongId = SongId,
                        DateCreated = DateTime.Now,
                        DateUpdated = DateTime.Now,
                        DisplayOrder = 1,
                        PictureId = picture.Id
                    };
                    _songService.InsertPicture(firstSongPicture);
                }
                else
                {
                    firstSongPicture.SongId = SongId;
                    firstSongPicture.DateCreated = DateTime.Now;
                    firstSongPicture.DateUpdated = DateTime.Now;
                    firstSongPicture.DisplayOrder = 1;
                    firstSongPicture.PictureId = picture.Id;
                    _songService.UpdatePicture(firstSongPicture);
                }

            }

            return Json(new { Success = true });
        }
        #endregion

        #region Utilities

        //page not found
        public ActionResult PageNotFound()
        {
            this.Response.StatusCode = 404;
            this.Response.TrySkipIisCustomErrors = true;

            return View();
        }


        /// <summary>
        /// Checks if current logged in user can actually edit the page
        /// </summary>
        /// <returns>True if editing is allowed. False otherwise</returns>
        [NonAction]
        bool CanEdit(Song Song)
        {
            if (Song == null)
                return false;
            return _workContext.CurrentCustomer.Id == Song.PageOwnerId //page owner
                || _workContext.CurrentCustomer.IsAdmin(); //administrator
        }

        /// <summary>
        /// Checks if current logged in user can actually delete the page
        /// </summary>
        /// <returns>True if deletion is allowed. False otherwise</returns>
        [NonAction]
        bool CanDelete(Song Song)
        {
            if (Song == null)
                return false;
            return _workContext.CurrentCustomer.Id == Song.PageOwnerId //page owner
                || _workContext.CurrentCustomer.IsAdmin(); //administrator
        }

        [NonAction]
        Song SaveRemoteSongToDB(string songJson)
        {
            if (string.IsNullOrEmpty(songJson))
                return null;

            var song = (JObject)JsonConvert.DeserializeObject(songJson);
           
            var songPage = new Song() {
                PageOwnerId = _workContext.CurrentCustomer.IsAdmin() ? _workContext.CurrentCustomer.Id : 0,
                Description = song["Description"].ToString(),
                Name = song["Name"].ToString(),
                RemoteEntityId = song["RemoteEntityId"].ToString(),
                RemoteSourceName = song["RemoteSourceName"].ToString(),
                PreviewUrl = song["PreviewUrl"].ToString(),
                TrackId = song["TrackId"].ToString(),
                RemoteArtistId = song["ArtistId"].ToString()
            };

            _songService.Insert(songPage);

            //we can now download the image from the server and store it on our own server
            //use the json we retrieved earlier

            if (!string.IsNullOrEmpty(song["ImageUrl"].ToString()))
            {
                var imageUrl = song["ImageUrl"].ToString();
                var imageBytes = HttpHelper.ExecuteGET(imageUrl);
                if (imageBytes != null)
                {
                var fileExtension = Path.GetExtension(imageUrl);
                if (!String.IsNullOrEmpty(fileExtension))
                    fileExtension = fileExtension.ToLowerInvariant();

                var contentType = PictureUtility.GetContentType(fileExtension);

                    var picture = _pictureService.InsertPicture(imageBytes, contentType, songPage.GetSeName(_workContext.WorkingLanguage.Id, true, false), true);
                    var songPicture = new SongPicture() {
                        SongId = songPage.Id,
                    DateCreated = DateTime.Now,
                    DateUpdated = DateTime.Now,
                    DisplayOrder = 1,
                    PictureId = picture.Id
                };
                    _songService.InsertPicture(songPicture);
                }
               
            }
            return songPage;
            
        }

        #endregion

      

    }
}
