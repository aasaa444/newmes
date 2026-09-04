IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AuditEntries] (
    [Id] bigint NOT NULL IDENTITY,
    [OccurredAt] datetimeoffset NOT NULL,
    [Action] nvarchar(64) NOT NULL,
    [ActorUserName] nvarchar(64) NOT NULL,
    [SubjectType] nvarchar(64) NULL,
    [SubjectId] nvarchar(128) NULL,
    [Detail] nvarchar(max) NULL,
    CONSTRAINT [PK_AuditEntries] PRIMARY KEY ([Id])
);

CREATE TABLE [ErpOutboxMessages] (
    [Id] uniqueidentifier NOT NULL,
    [MessageType] nvarchar(64) NOT NULL,
    [BusinessKey] nvarchar(128) NULL,
    [PayloadJson] nvarchar(max) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [Status] nvarchar(32) NOT NULL,
    CONSTRAINT [PK_ErpOutboxMessages] PRIMARY KEY ([Id])
);

CREATE TABLE [Materials] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(64) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [IsFinishedGood] bit NOT NULL,
    [IsKeyComponent] bit NOT NULL,
    [RequiresSerialNumber] bit NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Materials] PRIMARY KEY ([Id])
);

CREATE TABLE [ProductionLines] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(64) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_ProductionLines] PRIMARY KEY ([Id])
);

CREATE TABLE [Users] (
    [Id] uniqueidentifier NOT NULL,
    [UserName] nvarchar(64) NOT NULL,
    [DisplayName] nvarchar(128) NOT NULL,
    [PasswordHash] nvarchar(200) NOT NULL,
    [Role] nvarchar(32) NOT NULL,
    [IsActive] bit NOT NULL,
    [CanViewOpsOverview] bit NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);

CREATE TABLE [Boms] (
    [Id] uniqueidentifier NOT NULL,
    [FinishedMaterialId] uniqueidentifier NOT NULL,
    [Version] nvarchar(16) NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Boms] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Boms_Materials_FinishedMaterialId] FOREIGN KEY ([FinishedMaterialId]) REFERENCES [Materials] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [FinishedGoodsInventories] (
    [Id] uniqueidentifier NOT NULL,
    [MaterialId] uniqueidentifier NOT NULL,
    [QuantityOnHand] decimal(18,4) NOT NULL,
    CONSTRAINT [PK_FinishedGoodsInventories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FinishedGoodsInventories_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [Materials] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [LineSideInventories] (
    [Id] uniqueidentifier NOT NULL,
    [MaterialId] uniqueidentifier NOT NULL,
    [QuantityOnHand] decimal(18,4) NOT NULL,
    CONSTRAINT [PK_LineSideInventories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LineSideInventories_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [Materials] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ProcessRoutes] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(64) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [FinishedMaterialId] uniqueidentifier NOT NULL,
    [Version] nvarchar(16) NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_ProcessRoutes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProcessRoutes_Materials_FinishedMaterialId] FOREIGN KEY ([FinishedMaterialId]) REFERENCES [Materials] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [BomLines] (
    [Id] uniqueidentifier NOT NULL,
    [BomId] uniqueidentifier NOT NULL,
    [ComponentMaterialId] uniqueidentifier NOT NULL,
    [QuantityPer] decimal(18,4) NOT NULL,
    CONSTRAINT [PK_BomLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BomLines_Boms_BomId] FOREIGN KEY ([BomId]) REFERENCES [Boms] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_BomLines_Materials_ComponentMaterialId] FOREIGN KEY ([ComponentMaterialId]) REFERENCES [Materials] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ProcessSteps] (
    [Id] uniqueidentifier NOT NULL,
    [ProcessRouteId] uniqueidentifier NOT NULL,
    [Sequence] int NOT NULL,
    [Code] nvarchar(64) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [IsQualityStep] bit NOT NULL,
    [ReworkToSequence] int NULL,
    CONSTRAINT [PK_ProcessSteps] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProcessSteps_ProcessRoutes_ProcessRouteId] FOREIGN KEY ([ProcessRouteId]) REFERENCES [ProcessRoutes] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [WorkOrders] (
    [Id] uniqueidentifier NOT NULL,
    [OrderNo] nvarchar(64) NOT NULL,
    [FinishedMaterialId] uniqueidentifier NOT NULL,
    [PlannedQty] decimal(18,4) NOT NULL,
    [CompletedQty] decimal(18,4) NOT NULL,
    [ScrappedQty] decimal(18,4) NOT NULL,
    [Status] int NOT NULL,
    [ProcessRouteId] uniqueidentifier NULL,
    [FrozenRouteVersion] nvarchar(16) NULL,
    [BomId] uniqueidentifier NULL,
    [FrozenBomVersion] nvarchar(16) NULL,
    [InProcessSerialCount] int NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [ReleasedAt] datetimeoffset NULL,
    CONSTRAINT [PK_WorkOrders] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_WorkOrders_Boms_BomId] FOREIGN KEY ([BomId]) REFERENCES [Boms] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrders_Materials_FinishedMaterialId] FOREIGN KEY ([FinishedMaterialId]) REFERENCES [Materials] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrders_ProcessRoutes_ProcessRouteId] FOREIGN KEY ([ProcessRouteId]) REFERENCES [ProcessRoutes] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [WorkStations] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(64) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [ProductionLineId] uniqueidentifier NOT NULL,
    [BoundProcessStepId] uniqueidentifier NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_WorkStations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_WorkStations_ProcessSteps_BoundProcessStepId] FOREIGN KEY ([BoundProcessStepId]) REFERENCES [ProcessSteps] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkStations_ProductionLines_ProductionLineId] FOREIGN KEY ([ProductionLineId]) REFERENCES [ProductionLines] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ProductSerials] (
    [Id] uniqueidentifier NOT NULL,
    [SerialNo] nvarchar(64) NOT NULL,
    [WorkOrderId] uniqueidentifier NOT NULL,
    [CurrentProcessStepId] uniqueidentifier NULL,
    [Status] int NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ProductSerials] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProductSerials_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [WorkOrderIssueLines] (
    [Id] uniqueidentifier NOT NULL,
    [WorkOrderId] uniqueidentifier NOT NULL,
    [MaterialId] uniqueidentifier NOT NULL,
    [IssuedQty] decimal(18,4) NOT NULL,
    [PendingQty] decimal(18,4) NOT NULL,
    [ConsumedQty] decimal(18,4) NOT NULL,
    CONSTRAINT [PK_WorkOrderIssueLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_WorkOrderIssueLines_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [Materials] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrderIssueLines_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [ComponentBindings] (
    [Id] uniqueidentifier NOT NULL,
    [ProductSerialId] uniqueidentifier NOT NULL,
    [ComponentMaterialId] uniqueidentifier NOT NULL,
    [ComponentSerialNo] nvarchar(64) NOT NULL,
    [ProcessStepId] uniqueidentifier NULL,
    [BoundAt] datetimeoffset NOT NULL,
    [OperatorUserName] nvarchar(64) NULL,
    CONSTRAINT [PK_ComponentBindings] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ComponentBindings_Materials_ComponentMaterialId] FOREIGN KEY ([ComponentMaterialId]) REFERENCES [Materials] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ComponentBindings_ProductSerials_ProductSerialId] FOREIGN KEY ([ProductSerialId]) REFERENCES [ProductSerials] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [SerialPassRecords] (
    [Id] uniqueidentifier NOT NULL,
    [ProductSerialId] uniqueidentifier NOT NULL,
    [ProcessStepId] uniqueidentifier NOT NULL,
    [WorkStationId] uniqueidentifier NULL,
    [Result] nvarchar(16) NOT NULL,
    [OperatorUserName] nvarchar(64) NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [Remark] nvarchar(max) NULL,
    CONSTRAINT [PK_SerialPassRecords] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SerialPassRecords_ProcessSteps_ProcessStepId] FOREIGN KEY ([ProcessStepId]) REFERENCES [ProcessSteps] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SerialPassRecords_ProductSerials_ProductSerialId] FOREIGN KEY ([ProductSerialId]) REFERENCES [ProductSerials] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_SerialPassRecords_WorkStations_WorkStationId] FOREIGN KEY ([WorkStationId]) REFERENCES [WorkStations] ([Id]) ON DELETE SET NULL
);

CREATE INDEX [IX_AuditEntries_OccurredAt] ON [AuditEntries] ([OccurredAt]);

CREATE INDEX [IX_BomLines_BomId] ON [BomLines] ([BomId]);

CREATE INDEX [IX_BomLines_ComponentMaterialId] ON [BomLines] ([ComponentMaterialId]);

CREATE INDEX [IX_Boms_FinishedMaterialId] ON [Boms] ([FinishedMaterialId]);

CREATE INDEX [IX_ComponentBindings_ComponentMaterialId] ON [ComponentBindings] ([ComponentMaterialId]);

CREATE UNIQUE INDEX [IX_ComponentBindings_ComponentSerialNo] ON [ComponentBindings] ([ComponentSerialNo]);

CREATE INDEX [IX_ComponentBindings_ProductSerialId] ON [ComponentBindings] ([ProductSerialId]);

CREATE UNIQUE INDEX [IX_FinishedGoodsInventories_MaterialId] ON [FinishedGoodsInventories] ([MaterialId]);

CREATE UNIQUE INDEX [IX_LineSideInventories_MaterialId] ON [LineSideInventories] ([MaterialId]);

CREATE UNIQUE INDEX [IX_Materials_Code] ON [Materials] ([Code]);

CREATE UNIQUE INDEX [IX_ProcessRoutes_Code] ON [ProcessRoutes] ([Code]);

CREATE INDEX [IX_ProcessRoutes_FinishedMaterialId] ON [ProcessRoutes] ([FinishedMaterialId]);

CREATE UNIQUE INDEX [IX_ProcessSteps_ProcessRouteId_Sequence] ON [ProcessSteps] ([ProcessRouteId], [Sequence]);

CREATE UNIQUE INDEX [IX_ProductionLines_Code] ON [ProductionLines] ([Code]);

CREATE UNIQUE INDEX [IX_ProductSerials_SerialNo] ON [ProductSerials] ([SerialNo]);

CREATE INDEX [IX_ProductSerials_WorkOrderId] ON [ProductSerials] ([WorkOrderId]);

CREATE INDEX [IX_SerialPassRecords_ProcessStepId] ON [SerialPassRecords] ([ProcessStepId]);

CREATE INDEX [IX_SerialPassRecords_ProductSerialId] ON [SerialPassRecords] ([ProductSerialId]);

CREATE INDEX [IX_SerialPassRecords_WorkStationId] ON [SerialPassRecords] ([WorkStationId]);

CREATE UNIQUE INDEX [IX_Users_UserName] ON [Users] ([UserName]);

CREATE INDEX [IX_WorkOrderIssueLines_MaterialId] ON [WorkOrderIssueLines] ([MaterialId]);

CREATE UNIQUE INDEX [IX_WorkOrderIssueLines_WorkOrderId_MaterialId] ON [WorkOrderIssueLines] ([WorkOrderId], [MaterialId]);

CREATE INDEX [IX_WorkOrders_BomId] ON [WorkOrders] ([BomId]);

CREATE INDEX [IX_WorkOrders_FinishedMaterialId] ON [WorkOrders] ([FinishedMaterialId]);

CREATE UNIQUE INDEX [IX_WorkOrders_OrderNo] ON [WorkOrders] ([OrderNo]);

CREATE INDEX [IX_WorkOrders_ProcessRouteId] ON [WorkOrders] ([ProcessRouteId]);

CREATE INDEX [IX_WorkStations_BoundProcessStepId] ON [WorkStations] ([BoundProcessStepId]);

CREATE UNIQUE INDEX [IX_WorkStations_Code] ON [WorkStations] ([Code]);

CREATE INDEX [IX_WorkStations_ProductionLineId] ON [WorkStations] ([ProductionLineId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260729090516_InitialLegacyBaseline', N'10.0.10');

COMMIT;
GO

