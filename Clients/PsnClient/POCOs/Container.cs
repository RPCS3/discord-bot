using System;

namespace PsnClient.POCOs
{
    public class Container
    {
        public ContainerData Data;
        public ContainerIncluded[] Included;
    }

    public class ContainerData
    {
        public string Id;
        public string Type;
        public ContainerDataAttributes Attributes;
        public Relationships Relationships;
    }

    public class ContainerDataAttributes
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

    public class ContainerFacet
    {
        public string Name;
        public ContainerFacetItem[] Items;
    }

    public class ContainerFacetItem
    {
        public string Key;
        public string Name;
        public int Count;
    }

    public class ContainerBanner { }
    public class ContainerPromoBackground { }
    public class ContainerDataAttributesSubScenes { }

    public class ContainerIncluded
    {
        public string Id;
        public string Type;
        public ContainerIncludedAttributes Attributes;
        public Relationships Relationships;
    }

    public class ContainerIncludedAttributes
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

    public class GameFileSize
    {
        public string Unit;
        public decimal? Value;
    }

    public class GameMediaList
    {
        public GameMediaPreview[] Preview;
        public GameMediaPromo Promo;
        public GameMediaLink[] Screenshots;
    }

    public class GameMediaPreview { }

    public class GameMediaPromo
    {
        public GameMediaLink[] Images;
        public GameMediaLink[] Videos;
    }

    public class GameMediaLink
    {
        public string Url;
    }

    public class GameParent
    {
        public string Id;
        public string Name;
        public string Thumbnail;
        public string Url;
    }

    public class GameSku
    {
        public string Id;
        public string Name;
        public bool IsPreorder;
        public bool? Multibuy;
        public DateTime? PlayabilityDate;
        public GameSkuPrices Prices;
    }

    public class GameSkuPrices
    {
        public GameSkuPricesInfo NonPlusUser;
        public GameSkuPricesInfo PlusUser;
    }

    public class GameSkuPricesInfo
    {
        public GamePriceInfo ActualPrice;
        public GamePriceAvailability Availability;
        public decimal DiscountPercentage;
        public bool IsPlus;
        public GamePriceInfo StrikeThroughPrice;
        public GamePriceInfo UpsellPrice;
    }

    public class GamePriceInfo
    {
        public string Display;
        public decimal Value;
    }

    public class GamePriceAvailability
    {
        public DateTime? StartDate;
        public DateTime? EndDate;
    }

    public class GameStarRating
    {
        public decimal Score;
        public int Total;
    }

    public class GameLanguageCode
    {
        public string Name;
        public string[] Codes;
    }

    public class GameUpsellInfo
    {
        public string Type;
        public string DisplayPrice;
        public bool IsFree;
        public decimal DiscountPercentageDifference;
    }

    public class GameSkuRelation
    {
        public string Id;
        public string Name;
    }
}
