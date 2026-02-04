namespace SharepointExplorer.Services
{
    public static class AppServices
    {
        public static ISharePointService SharePoint { get; } = new SharePointService();
    }
}