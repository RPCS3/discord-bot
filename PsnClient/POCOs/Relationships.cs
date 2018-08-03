namespace PsnClient.POCOs
{
    public class Relationships
    {
        public RelationshipsChildren Children;
        public RelationshipsLegacySkus LegacySkus;
    }

    public class RelationshipsChildren
    {
        public RelationshipsChildrenItem[] Data;
    }

    public class RelationshipsChildrenItem
    {
        public string Id;
        public string Type;
    }

    public class RelationshipsLegacySkus
    {
        public RelationshipsChildrenItem[] Data;
    }
}