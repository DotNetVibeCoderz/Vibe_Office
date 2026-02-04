using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using SharepointExplorer.Models;

namespace SharepointExplorer.Services
{
    public interface ISharePointService
    {
        Task<bool> ConnectAsync(string siteUrl, string username, SecureString password);
        Task<bool> ConnectInteractiveAsync(string siteUrl, string clientId, string redirectUri);
        Task<List<SPNode>> GetStructureAsync(string relativeUrl = "");
        Task<List<SPNode>> GetFolderContentsAsync(string serverRelativeUrl);
        Task CreateFolderAsync(string serverRelativeUrl, string newFolderName);
        Task DeleteItemAsync(string serverRelativeUrl, SPItemType type);
        Task RenameItemAsync(string serverRelativeUrl, string newName, SPItemType type);
        Task<Stream> DownloadFileAsync(string serverRelativeUrl);
        Task UploadFileAsync(string folderServerRelativeUrl, string fileName, Stream content);
        string GetCurrentSiteUrl();
    }
}