/*
 *
 * (c) Copyright Ascensio System Limited 2010-2018
 *
 * The MIT License (MIT)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
*/

using System;
using System.Web;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Net;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.SharePoint;
using Microsoft.SharePoint.Administration;
using Microsoft.SharePoint.Utilities;
using Microsoft.SharePoint.WebControls;
using Onlyoffice;

namespace Onlyoffice.Layouts
{
    public partial class editorPage : LayoutsPageBase
    {
        protected string Key = "YQXK78GQD4FF", //random for initialization
                         FileName = "",
                         FileType = "",
                         FileAuthor = "",
                         FileTimeCreated = "",
                         FileEditorMode = "view",
                         urlDocDownload = "",
                         documentType = "",
                         urlDocTrack = "",
                         GoToBack = "",
                         GoToBackText = "",
                         lang = "",
                         CurrentUserName = "",
                         CurrentUserLogin = "",
                         SPListItemId, SPListURLDir, SPSource, SPListId, Folder, Secret,
                         DocumentSeverHost = "@http://localhost",
                         host = HttpUtility.HtmlEncode(HttpContext.Current.Request.Url.Scheme) + "://" + HttpContext.Current.Request.Url.Authority,
                         SPUrl = HttpUtility.HtmlEncode(HttpContext.Current.Request.Url.Scheme) + "://" + HttpContext.Current.Request.Url.Authority +
                                   HttpContext.Current.Request.RawUrl.Substring(0, HttpContext.Current.Request.RawUrl.IndexOf("_layouts")),
                         SPVersion = SPFarm.Local.BuildVersion.Major == 14 ? "" : "15/";

        protected int CurrentUserId = 0;
        protected bool canEdit = false;
        SPUser currentUser;
        protected void Page_Load(object sender, EventArgs e)
        {
            SPListItemId = Request["SPListItemId"];
            SPListURLDir = Request["SPListURLDir"];
            SPListId = Request["SPListId"];
            SPSource = Request["SPSource"];

            currentUser = SPContext.Current.Web.CurrentUser;
            CurrentUserId = currentUser.ID;
            CurrentUserName = currentUser.Name;
            
            if(currentUser == null)
            {
                Response.Redirect(SPUrl, true);
                return;
            }
            SPList list = null;
            SPListItem item = null;
            try
            {
                list = SPContext.Current.Web.GetList(SPListURLDir);
                item = list.GetItemById(Int32.Parse(SPListItemId));
            }
            catch(NullReferenceException ex) { Log.LogError(ex.Message); }
            if (item == null)
            {
                Response.Redirect(SPUrl, true);
                return;
            }

            // Make sure user has permissions to view the item
            if (!item.DoesUserHavePermissions(currentUser, SPBasePermissions.ViewListItems))
            {
                Response.Redirect(SPUrl, true);
                return;
            }
            canEdit = item.DoesUserHavePermissions(currentUser, SPBasePermissions.EditListItems);

            SPSecurity.RunWithElevatedPrivileges(delegate ()
            {
                using (SPSite site = new SPSite(SPUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        //check secret key
                        //==================================================================================
                        if (web.Properties["SharePointSecret"] == null)
                        {
                            var rnd = new Random();
                            var spSecret = "";
                            for (var i = 0; i < 6; i++)
                            {
                                spSecret = spSecret + rnd.Next(1, 9).ToString();
                            }
                            web.AllowUnsafeUpdates = true;
                            web.Update();
                            web.Properties.Add("SharePointSecret", spSecret);
                            web.Properties.Update();
                            web.AllowUnsafeUpdates = true;
                            web.Update();
                        }
                        Secret = web.Properties["SharePointSecret"];

                        //read settings
                        //==================================================================================
                        if (web.Properties["DocumentServerHost"] != null)
                        {
                            DocumentSeverHost = web.Properties["DocumentServerHost"];
                        }
                        DocumentSeverHost += DocumentSeverHost.EndsWith("/") ? "" : "/";

                        var lcid = (int)web.Language;
                        var defaultCulture = new CultureInfo(lcid);
                        lang = defaultCulture.IetfLanguageTag;

                        GoToBackText = LoadResource("GoToBack");
                        
                        //generate key and get file info for DocEditor
                        //==================================================================================
                        try
                        {
                            SPFile file = item.File;
                            if (file != null)
                            {
                                Key = file.ETag;
                                Key = GenerateRevisionId(Key);

                                Folder = Path.GetDirectoryName(file.ServerRelativeUrl);
                                Folder = Folder.Replace("\\", "/");
                                GoToBack = host + Folder;
                                SPUser author = file.Author;
                                FileAuthor = author.Name;

                                var tzi = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneInfo.Local.Id);
                                FileTimeCreated = TimeZoneInfo.ConvertTimeFromUtc(file.TimeCreated, tzi).ToString();

                                FileName = file.Name;

                                var tmp = FileName.Split('.');
                                FileType = tmp[tmp.Length - 1];

                                //check document format
                                try
                                {
                                    if (FileUtility.CanViewTypes.Contains(FileType))
                                    {
                                        var canEditType = FileUtility.CanEditTypes.Contains(FileType);
                                        canEdit = canEdit & canEditType;
                                        FileEditorMode = canEdit == true ? "edit" : FileEditorMode;
                                        //documentType = FileUtility.docTypes[FileType];   DocType.GetDocType(FileName)   
                                        documentType = FileUtility.GetDocType(FileType);
                                    }
                                    else
                                    {
                                        Response.Redirect(SPUrl);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    //if a error - redirect to home page
                                    Log.LogError(ex.Message);
                                    Response.Redirect(SPUrl);
                                }
                            }
                            else
                            {
                                Response.Redirect(SPUrl);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.LogError(ex.Message);
                            Response.Redirect(SPUrl + "/_layouts/" + SPVersion + "error.aspx");
                        }
                    }
                }
            });

            //generate url hash 
            //==================================================================================  
            urlDocDownload = Encryption.GetUrlHash(SPListItemId, Folder, SPListURLDir, "download", Secret);
            urlDocTrack = Encryption.GetUrlHash(SPListItemId, Folder, SPListURLDir, "track", Secret);
        }

        /// <summary>
        /// Translation key to a supported form.
        /// </summary>
        /// <param name="expectedKey">Expected key</param>
        /// <returns>Supported key</returns>
        public static string GenerateRevisionId(string expectedKey)
        {
            expectedKey = expectedKey ?? "";
            const int maxLength = 20;
            if (expectedKey.Length > maxLength) expectedKey = Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(expectedKey)));
            var key = Regex.Replace(expectedKey, "[^0-9a-zA-Z_]", "_");
            return key.Substring(key.Length - Math.Min(key.Length, maxLength));
        }

        private string LoadResource(string _resName)
        {
            return Microsoft.SharePoint.Utilities.SPUtility.GetLocalizedString("$Resources:Resource," + _resName,
                "core", (uint)SPContext.Current.Web.UICulture.LCID);
        }
    }
}
