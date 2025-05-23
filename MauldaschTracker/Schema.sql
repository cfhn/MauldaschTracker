CREATE TABLE MauldaschTracker.dbo.Collection (
	Id uniqueidentifier NOT NULL,
	Name nvarchar(1000) NOT NULL,
	ParentCollectionId uniqueidentifier NULL,
	CONSTRAINT Collection_PK PRIMARY KEY (Id),
	CONSTRAINT Collection_ParentCollection_FK FOREIGN KEY (ParentCollectionId) REFERENCES MauldaschTracker.dbo.Collection(Id)
);

CREATE TABLE MauldaschTracker.dbo.Item (
	Id uniqueidentifier NOT NULL,
	Owner nvarchar(1000) NOT NULL,
	Name nvarchar(1000) NOT NULL,
	Description nvarchar(MAX) NULL,
	ParentCollectionId uniqueidentifier NULL,
	CONSTRAINT Item_PK PRIMARY KEY (Id),
	CONSTRAINT Item_Collection_FK FOREIGN KEY (ParentCollectionId) REFERENCES MauldaschTracker.dbo.Collection(Id)
);

CREATE TABLE MauldaschTracker.dbo.TrackingPosition (
	ItemId uniqueidentifier NOT NULL,
	[Time] datetime2(0) NOT NULL,
	CollectionId uniqueidentifier NULL,
	Latitude decimal(9,6) NULL,
	Longitude decimal(9,6) NULL,
	CONSTRAINT TrackingPosition_Collection_FK FOREIGN KEY (CollectionId) REFERENCES MauldaschTracker.dbo.Collection(Id),
	CONSTRAINT TrackingPosition_Item_FK FOREIGN KEY (ItemId) REFERENCES MauldaschTracker.dbo.Item(Id) ON DELETE CASCADE
);
