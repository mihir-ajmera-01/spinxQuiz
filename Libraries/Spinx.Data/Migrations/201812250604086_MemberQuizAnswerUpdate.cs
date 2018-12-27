namespace Spinx.Data.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class MemberQuizAnswerUpdate : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.MemberQuizAnswers", "IsAttempt", c => c.Boolean());
        }
        
        public override void Down()
        {
            DropColumn("dbo.MemberQuizAnswers", "IsAttempt");
        }
    }
}
