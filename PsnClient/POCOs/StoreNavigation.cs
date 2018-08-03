namespace PsnClient.POCOs
{
    public class StoreNavigation
    {
        public StoreNavigationData Data;
        //public StoreNavigationIncluded Included;
    }

    public class StoreNavigationData
    {
        public string Id;
        public string Type;
        public StoreNavigationAttributes Attributes;
        public Relationships Relationships;
    }

    public class StoreNavigationAttributes
    {
        public string Name;
        public StoreNavigationNavigation[] Navigation;
    }

    public class StoreNavigationNavigation
    {
        public string Id;
        public string Name;
        public string TargetContainerId;
        public string RouteName;
        public StoreNavigationSubmenu[] Submenu;
    }

    public class StoreNavigationSubmenu
    {
        public string Name;
        public string TargetContainerId;
        public int? TemplateDefId;
        public StoreNavigationSubmenuItem[] Items;
    }

    public class StoreNavigationSubmenuItem
    {
        public string Name;
        public string TargetContainerId;
        public string TargetContainerType;
        public int? TemplateDefId;
        public bool IsSeparator;
    }
}
