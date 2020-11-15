using System;

namespace PsnClient.POCOs
{
#nullable disable
    public sealed class Container
    {
        public ContainerData Data;
        public ContainerIncluded[] Included;
    }

    public sealed class ContainerData
    {
        public string Id;
        public string Type;
        public ContainerDataAttributes Attributes;
        public Relationships Relationships;
    }

    public sealed class ContainerDataAttributes
    {
        public string Name;
        public bool? NsxPsPlusUpsell;
        public int? TemplateId;
        public string ThumbnailUrlBase;
        public int? Start;
        public int? Size;
        public int TotalResults;
        public string Query;
        public ContainerBanner[] Banners;
        public ContainerFacet[] Facets;
        public ContainerPromoBackground[] PromoBackgrounds;
        public ContainerDataAttributesSubScenes SubScenes;
    }

    public sealed class ContainerFacet
    {
        public string Name;
        public ContainerFacetItem[] Items;
    }

    public sealed class ContainerFacetItem
    {
        public string Key;
        public string Name;
        public int Count;
    }

    public sealed class ContainerBanner { }
    public sealed class ContainerPromoBackground { }
    public sealed class ContainerDataAttributesSubScenes { }

    public sealed class ContainerIncluded
    {
        public string Id;
        public string Type;
        public ContainerIncludedAttributes Attributes;
        public Relationships Relationships;
    }

    public sealed class ContainerIncludedAttributes
    {
        public string ContentType; // "1"
        public string DefaultSkuId;
        public bool DobRequired;
        public GameFileSize FileSize;
        public string GameContentType; // "Bundle"
        public string[] Genres;
        public bool? IsIgcUpsell;
        public bool? IsMultiplayerUpsell;
        public string KamajiRelationship; // "bundles"
        public string LegalText;
        public string LongDescription;
        public string MacrossBrainContext; // "game"
        public GameMediaList MediaList;
        public string Name;
        public GameParent Parent;
        public string[] Platforms; // "PS4"
        public string PrimaryClassification; // "PREMIUM_GAME"
        public string SecondaryClassification; // "GAME"
        public string TertiaryClassification; // "BUNDLE"
        public string ProviderName; // "EA Swiss Sarl"
        public string PsCameraCompatibility; // "incompatible"
        public string PsMoveCompatibility; // "incompatible"
        public string PsVrCompatibility; // "incompatible"
        public DateTime? ReleaseDate; // "2019-02-22T00:00:00Z"
        public GameSku[] Skus;
        public GameStarRating StarRating;
        public GameLanguageCode[] SubtitleLanguageCodes;
        public GameLanguageCode[] VoiceLanguageCodes;
        public string ThumbnailUrlBase;
        public string TopCategory; // "downloadable_game"
        public GameUpsellInfo UpsellInfo;
        // legacy-sku
        public GameSkuRelation[] Eligibilities;
        public GameSkuRelation[] Entitlements;
    }

    public sealed class GameFileSize
    {
        public string Unit;
        public decimal? Value;
    }

    public sealed class GameMediaList
    {
        public GameMediaPreview[] Preview;
        public GameMediaPromo Promo;
        public GameMediaLink[] Screenshots;
    }

    public sealed class GameMediaPreview { }

    public sealed class GameMediaPromo
    {
        public GameMediaLink[] Images;
        public GameMediaLink[] Videos;
    }

    public sealed class GameMediaLink
    {
        public string Url;
    }

    public sealed class GameParent
    {
        public string Id;
        public string Name;
        public string Thumbnail;
        public string Url;
    }

    public sealed class GameSku
    {
        public string Id;
        public string Name;
        public bool IsPreorder;
        public bool? Multibuy;
        public DateTime? PlayabilityDate;
        public GameSkuPrices Prices;
    }

    public sealed class GameSkuPrices
    {
        public GameSkuPricesInfo NonPlusUser;
        public GameSkuPricesInfo PlusUser;
    }

    public sealed class GameSkuPricesInfo
    {
        public GamePriceInfo ActualPrice;
        public GamePriceAvailability Availability;
        public decimal DiscountPercentage;
        public bool IsPlus;
        public GamePriceInfo StrikeThroughPrice;
        public GamePriceInfo UpsellPrice;
    }

    public sealed class GamePriceInfo
    {
        public string Display;
        public decimal Value;
    }

    public sealed class GamePriceAvailability
    {
        public DateTime? StartDate;
        public DateTime? EndDate;
    }

    public sealed class GameStarRating
    {
        public decimal Score;
        public int Total;
    }

    public sealed class GameLanguageCode
    {
        public string Name;
        public string[] Codes;
    }

    public sealed class GameUpsellInfo
    {
        public string Type;
        public string DisplayPrice;
        public bool IsFree;
        public decimal DiscountPercentageDifference;
    }

    public sealed class GameSkuRelation
    {
        public string Id;
        public string Name;
    }

    public sealed class FirmwareInfo
    {
        public string Version;
        public string DownloadUrl;
        public string Locale;
    }
    #nullable restore
}
