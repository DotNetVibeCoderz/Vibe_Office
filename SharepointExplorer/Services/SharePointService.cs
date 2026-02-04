using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Microsoft.SharePoint.Client;
using SharepointExplorer.Models;
using PnP.Framework;
using File = Microsoft.SharePoint.Client.File;

namespace SharepointExplorer.Services
{
    public class SharePointService : ISharePointService
    {
        private ClientContext? _context;
        private string _siteUrl = string.Empty;

        public string GetCurrentSiteUrl() => _siteUrl;

        public async Task<bool> ConnectAsync(string siteUrl, string username, SecureString password)
        {
            // Legacy authentication with username/password is often blocked or requires specific CSOM setup not present here.
            // We encourage using the Modern/Interactive Login.
            await Task.Delay(100); 
            Console.WriteLine("Legacy Auth not supported in this build. Use Modern Auth.");
            return false;
        }

        public async Task<bool> ConnectInteractiveAsync(string siteUrl, string clientId, string redirectUri)
        {
            try
            {
                _siteUrl = siteUrl;
                
                // If no client ID is provided, fall back to PnP Management Shell (though it might fail if not registered)
                if (string.IsNullOrWhiteSpace(clientId)) clientId = "31359c7f-bd7e-475c-86db-fdb8c937548e";
                if (string.IsNullOrWhiteSpace(redirectUri)) redirectUri = "http://localhost";
                
                // Use the Static Factory Method for Interactive Login
                var authManager = AuthenticationManager.CreateWithInteractiveLogin(
                    clientId, 
                    redirectUri, 
                    tenantId: null, 
                    azureEnvironment: PnP.Framework.AzureEnvironment.Production, 
                    tokenCacheCallback: null, 
                    customWebUi: null, 
                    useWAM: false
                );

                // Now GetContext(siteUrl) will use the interactive provider we just configured
                _context = await Task.Run(() => authManager.GetContext(siteUrl));

                if (_context != null)
                {
                    Web web = _context.Web;
                    _context.Load(web);
                    await _context.ExecuteQueryAsync();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                 Console.WriteLine("Interactive Connection Failed: " + ex.ToString());
                 return false;
            }
        }

        public async Task<List<SPNode>> GetStructureAsync(string relativeUrl = "")
        {
            var result = new List<SPNode>();
            if (_context == null) return result;
            try
            {
                Web web = _context.Web;
                _context.Load(web, w => w.Webs, w => w.Lists, w => w.ServerRelativeUrl);
                await _context.ExecuteQueryAsync();

                foreach (var subWeb in web.Webs)
                {
                    result.Add(new SPNode { Name = subWeb.Title, ServerRelativeUrl = subWeb.ServerRelativeUrl, Type = SPItemType.Web });
                }
                foreach (var list in web.Lists)
                {
                    // 101 = Document Library
                    if (list.BaseTemplate == 101 && !list.Hidden) 
                    {
                        result.Add(new SPNode { Name = list.Title, ServerRelativeUrl = list.RootFolder.ServerRelativeUrl, Type = SPItemType.Library });
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("GetStructure Error: " + ex.Message); }
            return result;
        }

        public async Task<List<SPNode>> GetFolderContentsAsync(string serverRelativeUrl)
        {
            var result = new List<SPNode>();
            if (_context == null) return result;
            try
            {
                Web web = _context.Web;
                Folder folder = web.GetFolderByServerRelativeUrl(serverRelativeUrl);
                _context.Load(folder, f => f.Folders, f => f.Files);
                await _context.ExecuteQueryAsync();

                foreach (var f in folder.Folders)
                {
                    result.Add(new SPNode { Name = f.Name, ServerRelativeUrl = f.ServerRelativeUrl, Type = SPItemType.Folder, LastModified = f.TimeLastModified, Size = 0 });
                }
                foreach (var f in folder.Files)
                {
                    result.Add(new SPNode { Name = f.Name, ServerRelativeUrl = f.ServerRelativeUrl, Type = SPItemType.File, LastModified = f.TimeLastModified, Size = f.Length });
                }
            }
            catch (Exception ex) { Console.WriteLine("GetFolderContents Error: " + ex.Message); }
            return result;
        }

        public async Task CreateFolderAsync(string serverRelativeUrl, string newFolderName)
        {
            if (_context == null) return;
            try
            {
                Web web = _context.Web;
                Folder parentFolder = web.GetFolderByServerRelativeUrl(serverRelativeUrl);
                parentFolder.Folders.Add(newFolderName);
                await _context.ExecuteQueryAsync();
            }
            catch (Exception ex) { Console.WriteLine("CreateFolder Error: " + ex.Message); }
        }

        public async Task DeleteItemAsync(string serverRelativeUrl, SPItemType type)
        {
            if (_context == null) return;
            try
            {
                Web web = _context.Web;
                if (type == SPItemType.File) { var f = web.GetFileByServerRelativeUrl(serverRelativeUrl); f.DeleteObject(); }
                else { var f = web.GetFolderByServerRelativeUrl(serverRelativeUrl); f.DeleteObject(); }
                await _context.ExecuteQueryAsync();
            }
            catch (Exception ex) { Console.WriteLine("DeleteItem Error: " + ex.Message); }
        }

        public async Task RenameItemAsync(string serverRelativeUrl, string newName, SPItemType type)
        {
             if (_context == null) return;
            try
            {
                Web web = _context.Web;
                string parentPath = serverRelativeUrl.Substring(0, serverRelativeUrl.LastIndexOf('/'));
                string newPath = $"{parentPath}/{newName}";
                if (type == SPItemType.File) { var f = web.GetFileByServerRelativeUrl(serverRelativeUrl); f.MoveTo(newPath, MoveOperations.Overwrite); }
                else { var f = web.GetFolderByServerRelativeUrl(serverRelativeUrl); f.MoveTo(newPath); }
                await _context.ExecuteQueryAsync();
            }
            catch (Exception ex) { Console.WriteLine("RenameItem Error: " + ex.Message); }
        }

        public async Task<Stream> DownloadFileAsync(string serverRelativeUrl)
        {
            if (_context == null) return Stream.Null;
            try
            {
                Web web = _context.Web;
                File file = web.GetFileByServerRelativeUrl(serverRelativeUrl);
                ClientResult<Stream> data = file.OpenBinaryStream();
                await _context.ExecuteQueryAsync();
                var ms = new MemoryStream();
                if (data.Value != null) { await data.Value.CopyToAsync(ms); ms.Position = 0; }
                return ms;
            }
            catch (Exception ex) { Console.WriteLine("DownloadFile Error: " + ex.Message); return Stream.Null; }
        }

        public async Task UploadFileAsync(string folderServerRelativeUrl, string fileName, Stream content)
        {
            if (_context == null) return;
            try
            {
                Web web = _context.Web;
                Folder folder = web.GetFolderByServerRelativeUrl(folderServerRelativeUrl);
                FileCreationInformation fci = new FileCreationInformation { ContentStream = content, Url = fileName, Overwrite = true };
                folder.Files.Add(fci);
                await _context.ExecuteQueryAsync();
            }
            catch (Exception ex) { Console.WriteLine("UploadFile Error: " + ex.Message); }
        }
    }
}