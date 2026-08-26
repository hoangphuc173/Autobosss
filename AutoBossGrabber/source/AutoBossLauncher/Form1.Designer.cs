namespace AutoBossLauncher
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.dgvAccounts = new System.Windows.Forms.DataGridView();
            this.colRunning = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.colUsername = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPassword = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colServer = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colCharacter = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colAutoLogin = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.colAutoHunting = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.colHeadless = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            
            this.btnSave = new System.Windows.Forms.Button();
            this.btnStart = new System.Windows.Forms.Button();
            this.txtLog = new System.Windows.Forms.RichTextBox();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.panelTop = new System.Windows.Forms.Panel();
            
            ((System.ComponentModel.ISupportInitialize)(this.dgvAccounts)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.panelTop.SuspendLayout();
            this.SuspendLayout();
            
            // dgvAccounts
            this.dgvAccounts.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvAccounts.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colRunning,
            this.colUsername,
            this.colPassword,
            this.colServer,
            this.colCharacter,
            this.colAutoLogin,
            this.colAutoHunting,
            this.colHeadless});
            this.dgvAccounts.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvAccounts.Location = new System.Drawing.Point(0, 40);
            this.dgvAccounts.Name = "dgvAccounts";
            this.dgvAccounts.Size = new System.Drawing.Size(784, 250);
            this.dgvAccounts.TabIndex = 0;
            
            // colRunning
            this.colRunning.DataPropertyName = "Running";
            this.colRunning.HeaderText = "Running";
            this.colRunning.Name = "colRunning";
            this.colRunning.Width = 60;
            
            // colUsername
            this.colUsername.DataPropertyName = "Username";
            this.colUsername.HeaderText = "Username";
            this.colUsername.Name = "colUsername";
            this.colUsername.Width = 120;
            
            // colPassword
            this.colPassword.DataPropertyName = "Password";
            this.colPassword.HeaderText = "Password";
            this.colPassword.Name = "colPassword";
            this.colPassword.Width = 120;
            
            // colServer
            this.colServer.DataPropertyName = "Server";
            this.colServer.HeaderText = "Server";
            this.colServer.Name = "colServer";
            this.colServer.Width = 60;
            
            // colCharacter
            this.colCharacter.DataPropertyName = "Character";
            this.colCharacter.HeaderText = "Char";
            this.colCharacter.Name = "colCharacter";
            this.colCharacter.Width = 50;

            // colAutoLogin
            this.colAutoLogin.DataPropertyName = "AutoLogin";
            this.colAutoLogin.HeaderText = "Auto Login";
            this.colAutoLogin.Name = "colAutoLogin";
            this.colAutoLogin.Width = 80;

            // colHeadless
            this.colHeadless.DataPropertyName = "Headless";
            this.colHeadless.HeaderText = "Headless";
            this.colHeadless.Name = "colHeadless";
            this.colHeadless.Width = 80;

            // colAutoHunting
            this.colAutoHunting.DataPropertyName = "AutoHunting";
            this.colAutoHunting.HeaderText = "Auto Farm";
            this.colAutoHunting.Name = "colAutoHunting";
            this.colAutoHunting.Width = 80;

            // panelTop
            this.panelTop.Controls.Add(this.btnSave);
            this.panelTop.Controls.Add(this.btnStart);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(784, 40);
            this.panelTop.TabIndex = 1;

            // btnSave
            this.btnSave.Location = new System.Drawing.Point(12, 8);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(100, 25);
            this.btnSave.TabIndex = 0;
            this.btnSave.Text = "Lưu (Save)";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);

            // btnStart
            this.btnStart.Location = new System.Drawing.Point(120, 8);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(120, 25);
            this.btnStart.TabIndex = 1;
            this.btnStart.Text = "Bắt đầu (Start)";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);

            // txtLog
            this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtLog.Location = new System.Drawing.Point(0, 0);
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.BackColor = System.Drawing.Color.Black;
            this.txtLog.ForeColor = System.Drawing.Color.Lime;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.txtLog.Size = new System.Drawing.Size(784, 267);
            this.txtLog.TabIndex = 0;
            this.txtLog.Text = "";

            // splitContainer1
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // splitContainer1.Panel1
            this.splitContainer1.Panel1.Controls.Add(this.dgvAccounts);
            this.splitContainer1.Panel1.Controls.Add(this.panelTop);
            // splitContainer1.Panel2
            this.splitContainer1.Panel2.Controls.Add(this.txtLog);
            this.splitContainer1.Size = new System.Drawing.Size(784, 561);
            this.splitContainer1.SplitterDistance = 290;
            this.splitContainer1.TabIndex = 2;

            // Form1
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 561);
            this.Controls.Add(this.splitContainer1);
            this.Name = "Form1";
            this.Text = "AutoBoss Launcher";
            ((System.ComponentModel.ISupportInitialize)(this.dgvAccounts)).EndInit();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.panelTop.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        private System.Windows.Forms.DataGridView dgvAccounts;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colRunning;
        private System.Windows.Forms.DataGridViewTextBoxColumn colUsername;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPassword;
        private System.Windows.Forms.DataGridViewTextBoxColumn colServer;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCharacter;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colAutoLogin;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colAutoHunting;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colHeadless;
        
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.RichTextBox txtLog;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.Panel panelTop;
    }
}
