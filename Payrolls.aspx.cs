using System;
using System.Collections;
using System.Configuration;
using System.Data;
using System.Web;
using System.Web.Security;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;
using System.Web.UI.WebControls.WebParts;
using System.Data.SqlClient;
using System.Drawing;
using DevExpress.Web;
using DevExpress.Web.Internal;
using System.IO;

namespace Infoglove.CommonData
{
    public partial class Payrolls : BasePage, INamingContainer
    {
        protected string masterEntity = "Payroll";
        protected string detailEntity = "PayrollRow";
        //private string documentLibrary = "\\DocumentLibrary\\Payrolls";
        private string otherMasterSearchColumn = "Payroll_Employee";
        protected Infoglove.MaintainMaster master;

        protected void Page_Load(object sender, System.EventArgs e)
        {
            if (this.master == null)
                this.master = new Infoglove.MaintainMaster(this, this.tbPageInstance);
            if (!this.master.TestSession(this.CompanyId.Text)) return;//Testaa, onko sessio hengissä ja ollaanko oikeassa yrityksessä

            Infoglove.Pages.Maintain.AdjustTabOrder(masterEntity, detailEntity, 100);//Aseta inputkenttien järjestys

            this.ShowDetailButtons();
            this.ShowModeMessage(false);

            if (IsCallback)
            {//Callback-toiminto
                Infoglove.Session.Property.callback = true;//Ohjaa mastersivun toimintaa
                this.ProcessCallback();
                Infoglove.Session.Property.callback = false;
                return;
            }
            Infoglove.Session.Property.callback = false;

            this.AdjustContainers();
            Infoglove.Pages.Maintain.TranslateCurrentPage();
            if (!this.IsPostBack)
            {//Tullaan toiselta sivulta, esim. hakusivulta
                this.ShowModeMessage(false);
                this.master.ClearOldPageInstances(this.masterEntity, this.detailEntity);
                Infoglove.Pages.Utils.StoreEntityDocumentIdToSession(null, this.masterEntity, string.Empty);
                this.ClearEmployeePayrollTypeList();
            }

            this.ProcessSearchRequest();//Hakukomentojen suoritus

        }

        private void ClearEmployeePayrollTypeList()
        {
            Infoglove.Session.Method.SetSessionObject(this.tbPageInstance.Text, "EmployeePayrollTypeList", null);
        }
        
        private void ProcessCallback()
        {//Suorita callback-toiminto
            string callbackParameter = this.CallbackParameter.Text;
            if (callbackParameter.Contains("[Get]"))
            {//Suorita hakutoiminto
                this.CallbackGet(callbackParameter);
                return;//Ei ShowDetailGrid:iä
            }

            if (callbackParameter.Contains("[Calculate]"))
            {//Suorita laskenta
                this.CallbackCalculate(callbackParameter);
                return;//Ei ShowDetailGrid:iä
            }

            if (callbackParameter.Contains("[Detail][Delete]"))
            {//Merkitse rivi poistetuksi
                using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                {
                    connection.Open();
                    this.AcceptDetailChanges(connection, Infoglove.Library.Constant.operationDeleteDetail);
                }

                this.master.InitializeEntity(this.detailEntity);
            }
            else if (callbackParameter.Contains("[Detail][Accept]"))
            {//Gridin päivitys muutetun rivin mukaiseksi
                using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                {
                    connection.Open();
                    if (this.AcceptDetailChanges(connection, string.Empty))
                    {//Rivi hyväksyttiin, initialisoi detail
                        this.master.InitializeEntity(this.detailEntity);
                        this.InitializePayrollRow(connection);
                    }
                }
                this.master.StoreInputControls(this.masterEntity);
            }
            else if (callbackParameter.Contains("[Detail][Insert]"))
            {//Uuden rivin alustus
                this.master.InitializeEntity(this.detailEntity);

                using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                {
                    connection.Open();
                    this.InitializePayrollRow(connection);
                }
            }

            this.CallbackParameter.Text = string.Empty;

            //Pelkkä tämä suoritetaan, kun valitaan gridiltä rivi, CallbackParameter on tällöin tyhjä
            this.ShowDetailGrid();
        }

        private void CallbackGet(string callbackParameter)
        {//Callbackilla suoritettava hakukomento, esim. [Payroll][Get][1]
            string directionString = Infoglove.Strings.Substring.From(callbackParameter, "[Get][");
            directionString = Infoglove.Strings.Substring.To(directionString, "]");//-2,-1,0,1,2
            if (callbackParameter.StartsWith("[Master]"))
            {//Masterin selaus
                string popSearchPayroll_CatalogSearch = Infoglove.Session.Method.GetSessionString("PopSearchPayroll_CatalogSearch");
                if (!string.IsNullOrEmpty(popSearchPayroll_CatalogSearch))
                {//Popup-ikkunassa on haettu jotakin, tarkista, onko katalogista
                    if (popSearchPayroll_CatalogSearch == "true")
                    {//Haku katalogista, hae siis katalogista primääriavain ja tiedot
                        //Tarkista ensin, että kataloginimike ei ole jo siirretty nimiketauluun, jolloin se haetaan sieltä
                        string item_PrimaryKey = Infoglove.Sql.Transaction.GetColumnByUniqId(null, "Payroll", "PrimaryKey", this.MasterSearch.Text);
                        if (string.IsNullOrEmpty(item_PrimaryKey))
                        {//Ei ole nimikerekisterissä, haettava katalogista ja annettava mahdollisuus perustaa uusi nimike
                            return;
                        }
                    }
                }

                this.SearchMasterEntity(directionString);
                this.CallbackParameter.Text = string.Empty;
                return;
            }

            if (callbackParameter.StartsWith("[SearchColumn]"))
            {//Pikahaun hakukenttä vaihtunut
                this.ShowMasterAndGrid(false);
                this.CallbackParameter.Text = string.Empty;
                this.master.StoreInputControls(this.masterEntity, this.detailEntity);
                return;
            }
            else if (callbackParameter.StartsWith("[PayrollType]"))
            {//Hae palkkalaji
                this.PayrollRow_PayrollType.DataBind();
                if (this.PayrollRow_PayrollType.SelectedItem != null)
                {//Alusta rivin kentät
                    using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                    {
                        connection.Open();
                        string fkPayrollType = Infoglove.Pages.Maintain.GetComboSelectedValue(this.PayrollRow_PayrollType);
                        DataTable dtPayrollRow = Infoglove.DataAccess.RestoreDtDetailEntity(Infoglove.Pages.Instance.GetPageInstance(this.tbPageInstance), this.detailEntity);
                        string fkEmployee = this.GetFKEmployee(connection);// Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_Employee);
                        this.InitializeRowNo();
                        string result = this.GetPayrollTypeData(connection, fkPayrollType, dtPayrollRow, fkEmployee);
                        if (Infoglove.Library.Method.IsError(result))
                        {//Virhetilanne
                            this.master.ShowErrorMessage(result);
                        }
                    }
                }

                this.CallbackParameter.Text = string.Empty;
                this.ShowDetailGrid();
                return;
            }
            else if (callbackParameter.StartsWith("[PayrollPeriod]"))
            {//Hae palkkakausi
                this.Payroll_PayrollPeriod.DataBind();
                if (this.Payroll_PayrollPeriod.SelectedItem != null)
                {//Alusta palkkalajisidonnaiset kentät
                    using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                    {
                        connection.Open();
                        string fkPayrollPeriod = Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_PayrollPeriod);
                        this.GetPayrollPeriodData(connection, fkPayrollPeriod);
                    }
                }

                this.CallbackParameter.Text = string.Empty;
                this.ShowDetailGrid();
                return;
            }
            else if (callbackParameter.StartsWith("[Employee]"))
            {//Hae palkansaaja
                this.ClearEmployeePayrollTypeList();
                this.Payroll_Employee.DataBind();
                if (this.Payroll_Employee.SelectedItem != null)
                {//Alusta palkansaajasidonnaiset kentät
                    using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                    {
                        connection.Open();
                        string fkEmployee = this.GetFKEmployee(connection);// Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_Employee);
                        this.GetEmployeeData(connection, fkEmployee);
                    }
                }

                this.CallbackParameter.Text = string.Empty;
                this.ShowDetailGrid();
                return;
            }
        
        }

        private string GetPayrollTypeData(SqlConnection connection, string fkPayrollType, DataTable dtPayrollRow, string fkEmployee)
        {//Hae palkkalajin takaa data editoitavalle riville
            if (string.IsNullOrEmpty(fkPayrollType))
                return string.Empty;

            //Alusta rivi tapahtumalajin perusteella
            Infoglove.Pages.Maintain.SetComboSelectedItem(this.PayrollRow_PayrollType, fkPayrollType);

            //Lisää tilit
            string fkAccount = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKAccount", fkPayrollType);
            Infoglove.Pages.Maintain.SetComboSelectedItem(this.PayrollRow_Account, fkAccount);

            string fkCreditAccount = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKCreditAccount", fkPayrollType);
            Infoglove.Pages.Maintain.SetComboSelectedItem(this.PayrollRow_CreditAccount, fkCreditAccount);

            string fkCostCenter = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKCostCenter", fkPayrollType);
            Infoglove.Pages.Maintain.SetComboSelectedItem(this.PayrollRow_CostCenter, fkCostCenter);

            //Selite, voidaan kirjoittaa yli
            this.PayrollRow_Description.Text = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "Description", fkPayrollType);//Kuvaus

            //Kentän Quantity laskenta, tässä lasketaan myös ennakonpidätysprosentti
            string quantity = string.Empty;
            string quantityFormula = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "QuantityFormula", fkPayrollType);

            //Kentän CalcValue laskenta
            string calcValue = string.Empty;
            string calcValueFormula = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "CalcValueFormula", fkPayrollType);

            this.PayrollRow_QuantityFormula.Text = quantityFormula;
            if (!string.IsNullOrEmpty(quantityFormula))
            {//Määrän laskentakaava olemassa, suorita laskenta

                decimal withholdIncome = this.CalculateWithholdIncome(calcValueFormula);//Laske pidätyksen alainen tulo, taulukko/prosentti tai molemmat
                string withholdIncomeField = this.GetWithholdIncomeField(calcValueFormula);

                quantity = this.CalculateQuantity(connection, withholdIncomeField, quantityFormula, fkEmployee, dtPayrollRow, fkPayrollType, withholdIncome);
                if (Infoglove.Library.Method.IsError(quantity))
                    return quantity;
            }
            this.PayrollRow_Quantity.Text = quantity;

            //Rivin yksikkö
            string fkUnit = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKUnit", fkPayrollType);
            if (!string.IsNullOrEmpty(fkUnit))
            {//Yksikkö on olemassa
                Infoglove.Pages.Maintain.SetComboSelectedItem(this.PayrollRow_Unit, fkUnit);
            }


            this.PayrollRow_CalcValueFormula.Text = calcValueFormula;//Esim Payroll_TableWithholdIncome+Payroll_PercentWithholdIncome
            if (!string.IsNullOrEmpty(calcValueFormula))
            {//Laskenta-arvon laskentakaava olemassa, suorita laskenta
                calcValue = this.CalculateCalcValue(connection, calcValueFormula, fkEmployee, dtPayrollRow);
            }
            this.PayrollRow_CalcValue.Text = calcValue;

            //Kentällä Coefficient voi poikkeustapauksessa olla laskentakaava, esim HolidayHourlyWageCoefficient. Yleensä kuitenkin kenttä sisältää vakion, esim 1,0000
            string coefficient = string.Empty;
            string coefficientFormula = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "CoefficientFormula", fkPayrollType);
            if (!string.IsNullOrEmpty(coefficientFormula))
            {//Kertoimen laskentakaava olemassa, suorita laskenta
                coefficient = this.CalculateCoefficient(connection, coefficientFormula, quantity);
            }
            this.PayrollRow_Coefficient.Text = coefficient;

            //Laske rivin kokonaisarvo
            decimal quantity_ = Infoglove.Numbers.Decimals.StringToDecimal(quantity);
            decimal calcValue_ = Infoglove.Numbers.Decimals.StringToDecimal(calcValue);
            decimal coefficient_ = Infoglove.Numbers.Decimals.StringToDecimal(coefficient);
            decimal rowValue = Infoglove.Business.Payroll.CalculateRowValue(connection,quantity_, ref calcValue_ ,coefficient_, this.PayrollRow_QuantityFormula.Text, fkEmployee, fkPayrollType);
            this.PayrollRow_RowValue.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(rowValue);
            this.PayrollRow_CalcValue.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(calcValue_);

            return string.Empty;

        }

        private decimal CalculateWithholdIncome(string calcValueFormula)
        {//Laske pidätyksen alainen tulo, käytössä paitsi ennakonpidätyksessä myös ay-maksussa, sairaskassan jäsenmaksussa jne
            if (calcValueFormula.Contains("TableWithholdIncome") && calcValueFormula.Contains("PercentWithholdIncome"))
            {//Molemmat, kyseessä on todennäköisimmin ay-maksu, sairaskassa jne, voi tietysti olla myös ennakonpidätys
                return Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_TableWithholdIncome.Text) + Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_PercentWithholdIncome.Text);
            }
            else if (calcValueFormula.Contains("TableWithholdIncome"))
            {//Taulukkopidätystulo
                return Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_TableWithholdIncome.Text);
            }
            else if (calcValueFormula.Contains("PercentWithholdIncome"))
            {//Prosenttipidätystulo
                return Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_PercentWithholdIncome.Text);

            }
            return 0m;//Ei mitään pidätyksenalaista tuloa

        }

        private string GetWithholdIncomeField(string calcValueFormula)
        {//Palauta pidätyksenalainen tulo sellaisessa muodossa, että tulosta voi käyttää SQL-lauseessa
            if (calcValueFormula.Contains("TableWithholdIncome") && calcValueFormula.Contains("PercentWithholdIncome"))
            {//Molemmat, kyseessä on todennäköisimmin ay-maksu, sairaskassa jne, voi tietysti olla myös ennakonpidätys
                return "TableWithholdIncome+PercentWithholdIncome";
            }
            else if (calcValueFormula.Contains("TableWithholdIncome"))
            {//Taulukkopidätystulo
                return "TableWithholdIncome";
            }
            else if (calcValueFormula.Contains("PercentWithholdIncome"))
            {//Prosenttipidätystulo
                return "PercentWithholdIncome";

            }
            return string.Empty;
        }
        
        private string GetEmployeeData(SqlConnection connection, string fkEmployee)
        {//Hae palkansaajan takaa dataa palkkatapahtumaotsikolle, tositteen lähetystapa ja kieli
            if (string.IsNullOrEmpty(fkEmployee))
                return string.Empty;

            string fkPayrollSendingType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "FKPayrollSendingType", fkEmployee);
            if (!string.IsNullOrEmpty(fkPayrollSendingType))
                Infoglove.Pages.Maintain.SetComboSelectedItem(this.Payroll_PayrollSendingType, fkPayrollSendingType);

            string fkLanguage = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "FKLanguage", fkEmployee);
            if (!string.IsNullOrEmpty(fkLanguage))
                Infoglove.Pages.Maintain.SetComboSelectedItem(this.Payroll_Language, fkLanguage);

            string socialSecurityPercent = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "SocialSecurityPercent", fkEmployee);
            this.Payroll_SocialSecurityPercent.Text = socialSecurityPercent;

            return string.Empty;
        }

        private string GetPayrollPeriodData(SqlConnection connection, string fkPayrollPeriod)
        {//Hae palkkakauden takaa dataa palkkatapahtumaotsikolle
            if (string.IsNullOrEmpty(fkPayrollPeriod))
                return string.Empty;

            DataTable dtPayrollPeriod = new DataTable();
            string selectString = "SELECT PeriodStartDate,PeriodEndDate,PaymentDate,WorkDaysMonth1,WorkDaysMonth2,FKPayrollPeriodType,HolidayAccDaysMonth1,HolidayAccDaysMonth2,WorkHours FROM PayrollPeriod WHERE PrimaryKey = " + fkPayrollPeriod;
            string result = Infoglove.Sql.Transaction.FillDataTable(connection, dtPayrollPeriod, selectString);
            if (dtPayrollPeriod.Rows.Count != 1)
                return string.Empty;

            this.Payroll_PeriodStartDate.Value = Infoglove.AdoNet.DataTable.DateValueNullIsNull(dtPayrollPeriod, 0, "PeriodStartDate");
            this.Payroll_PeriodEndDate.Value = Infoglove.AdoNet.DataTable.DateValueNullIsNull(dtPayrollPeriod, 0, "PeriodEndDate");
            this.Payroll_PaymentDate.Value = Infoglove.AdoNet.DataTable.DateValueNullIsNull(dtPayrollPeriod, 0, "PaymentDate");
            this.Payroll_WorkDaysMonth1.Text = Infoglove.AdoNet.DataTable.StringValue(dtPayrollPeriod, 0, "WorkDaysMonth1");
            this.Payroll_WorkDaysMonth2.Text = Infoglove.AdoNet.DataTable.StringValue(dtPayrollPeriod, 0, "WorkDaysMonth2");
            this.Payroll_HolidayAccDaysMonth1.Text = Infoglove.AdoNet.DataTable.StringValue(dtPayrollPeriod, 0, "HolidayAccDaysMonth1");
            this.Payroll_HolidayAccDaysMonth2.Text = Infoglove.AdoNet.DataTable.StringValue(dtPayrollPeriod, 0, "HolidayAccDaysMonth2");
            
            this.Payroll_WorkHours.Text = Infoglove.AdoNet.DataTable.StringValue(dtPayrollPeriod, 0, "WorkHours");

            //Veropäivät
            string fkPayrollPeriodType = Infoglove.AdoNet.DataTable.StringValue(dtPayrollPeriod, 0, "FKPayrollPeriodType");
            this.Payroll_TaxDayCount.Text = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollPeriodType", "TaxDayCount", fkPayrollPeriodType);

            //TyEl - prosentti
            string paymentDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PaymentDate.Text);
            decimal pensionFeePercent = Infoglove.Business.Payroll.GetTyElPercentGeneral(connection, paymentDate);//Vuonna 2018 tämä on 25.32%
            this.Payroll_PensionFeePercent.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(pensionFeePercent);

            return string.Empty;

        }

        private void SearchMasterEntity(string directionString)
        {//Hae pääentiteetti työkalupalkin toimintonäppäimellä
            //directionString on esim. SearchFirst, SearchLast jne, tai sitten -2, -1, 0, 1, 2

            this.ClearEmployeePayrollTypeList();

            this.rp_DocumentMap.Visible = false;//Ettei näy vääriä dokumentteja!

            string masterSearchText = this.MasterSearch.Text;
            this.FillMasterSearchColumn();
            string masterSearchColumn = Infoglove.Pages.Maintain.GetComboSelectedValue(this.MasterSearchColumn);

            this.Payroll_PrimaryKey.Text = Infoglove.Utils.SearchMasterEntity(ref masterSearchText, this.Payroll_UniqId.Text, directionString, masterSearchColumn);

            this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeMaintain);

            this.ShowMasterAndGrid(true);

            //masterSearchTextiin sijoitetaan UniqId vain jos se on hakukriteerinä
            if (this.MasterSearchColumn.SelectedItem.Value.ToString().Contains("UniqId"))
                this.MasterSearch.Text = this.Payroll_UniqId.Text;

            this.master.StoreInputControls(this.masterEntity, this.detailEntity);
            this.master.StoreInputControl(this.MasterSearch);

            if(false==true)//Ei taida tarvita enää, säilytä kuitenkin koodi!
                if (Infoglove.Library.Method.IsSuperUser())
                    if (directionString == "-1")
                    {//Superkäyttäjällä on tässä tilapäinen erikoisuus: Jos haetaan taaksepäin, niin tallennetaan samalla! Näin saadaan kanta konvertoiduksi jälkikäteen
                        this.SaveChanges();
                        this.ShowMasterAndGrid(true);
                        this.master.StoreInputControls(this.masterEntity, this.detailEntity);
                        this.master.StoreInputControl(this.MasterSearch);
                    }
        }

        public int GetMaxIntValueFromDtDetail(string entity, string columnId)
        {//Hae detailista suurin rivinumero lukuun ottamatta automaattirivejä, jotka ovat suurempia kuin 990
            //Saman niminen metodihan löytyy masterista, tässä on erona vain, että rivinumeron on oltava pienempi kuin 990
            int maxValue = 0;
            try
            {
                if (this.master.dtDetailEntity.Rows.Count == 0)
                    this.master.RestoreDtDetailEntity(entity);
                if (this.master.dtDetailEntity.Rows.Count == 0)
                    return maxValue;

                DataView dvDetailEntity = new DataView(this.master.dtDetailEntity);//Ei haluta sortata alkuperäistä datataulua, perustetaan siis aivan uusi DataView

                string originalSort = dvDetailEntity.Sort;
                dvDetailEntity.Sort = columnId + " DESC";
                foreach (DataRowView drv in dvDetailEntity)
                {//Maksimiarvo löytyy ekalta riviltä
                    maxValue = (int)drv[columnId];
                    if(maxValue < 990)
                        break;//Löytyi rivi, joka ei ole automaattirivi
                }
            }
            catch (Exception)
            {//Pieleen meni, ei haittaa
            }

            return maxValue;
        }

        private void InitializeRowNo()
        {//Rivinumeron alustus tarvittaessa
            if (this.PayrollRow_RowNo.Text.StartsWith("+") || string.IsNullOrEmpty(this.PayrollRow_RowNo.Text))
            {//Rivinumero kasvatetaan yleensä kymmenen välein
                string rowIncrement = Infoglove.Numbers.Integers.ForceToIntegerString(this.PayrollRow_RowNo.Text);//Yleensä +10
                if (string.IsNullOrEmpty(rowIncrement))
                    rowIncrement = "10";
                //hae detailin datataulusta suurin rivinumero
                int maxRowNo = this.GetMaxIntValueFromDtDetail(this.detailEntity, this.PayrollRow_RowNo.ID);
                int nextRowNo = maxRowNo + Convert.ToInt32(rowIncrement);
                this.PayrollRow_RowNo.Text = nextRowNo.ToString();
            }
        }

        private string GetFKEmployeeGroup(SqlConnection connection)
        {//Hae henkilön henkilöryhmän primääriavain
            string fkEmployee = this.GetFKEmployee(connection);
            string fkEmployeeGroup = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "FKEmployeeGroup", fkEmployee);
            return fkEmployeeGroup;
        }

        private string GetFKEmployee(SqlConnection connection)
        {//Hae henkilön primääriavain
            string fkEmployee = Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_Employee);
            if (string.IsNullOrEmpty(fkEmployee))
            {//Ei henkilöä, ei tapahtumalajia
                Infoglove.Pages.Maintain.FillComboSource(connection, "Employee");//Jos henkilön tietoja käydään muuttamassa kesken tositteen tallennuksen, niin combon sisältö häviää, Jaana löysi virheen 7.12.2017
                fkEmployee = Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_Employee);
                if (string.IsNullOrEmpty(fkEmployee))
                {//Nyt ei oikeastikaan ole henkilöä!
                    this.master.ShowErrorMessage("[ERROR] - Employee not selected");
                    return string.Empty;
                }
            }
            return fkEmployee;

        }


        private void InitializePayrollRow(System.Data.SqlClient.SqlConnection connection)
        {//Alusta rivin kentät ja hae henkilön seuraava vakiopalkkalaji

            this.master.StoreInputControl(this.PayrollRow_PrimaryKey);//Primääriavaimen oltava -1, jotta uuden rivin tallennus voisi tapahtua

            string fkEmployee = this.GetFKEmployee(connection);

            //Tähän mennessä tallennetut rivit on käytävä läpi, jotta tiedettäisiin, mitä palkkalajia tarjotaan seuraavaksi
            //Jos kuitenkin tositerivejä on jo tallennettu tietokantaan, uutta lajia ei enää tarjota
            string selectString = "SELECT Count(*) FROM PayrollRow WHERE FKPayroll = " + this.Payroll_PrimaryKey.Text;
            string payrollRowCountInDatabase = Infoglove.Sql.Transaction.GetString(connection, selectString);
            if (payrollRowCountInDatabase != "0")
            {//Rivejä tietokannassa, ei tarjota uutta vakiopalkkalajia
                return;
            }

            this.InitializeRowNo();

            //Hae henkilön seuraava vakiopalkkalaji, jonka alta hae tili, projekti, kustannuspaikka, ja laskentakaavat
            //Laskentakaavat voi saman tien suorittaa. Vasta SaveChanges() voinee tallentaa laskentakaavat myös palkkatapahtumariville, jos nyt tarpeen. Oscar ei tarvinnut!

            DataTable dtPayrollRow = Infoglove.DataAccess.RestoreDtDetailEntity(Infoglove.Pages.Instance.GetPageInstance(this.tbPageInstance), this.detailEntity);

            System.Collections.Generic.List<string> transactionPayrollTypeList = new System.Collections.Generic.List<string>();
            foreach (DataRow drPayrollRow in dtPayrollRow.Rows)
            {//Hae tämän hetken tilanne ja sen perusteella seuraava tapahtumalaji
                string fkPayrollType = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PayrollRow_FKPayrollType");
                if (!transactionPayrollTypeList.Contains(fkPayrollType))
                    transactionPayrollTypeList.Add(fkPayrollType);
            }

            //Hae henkilön vakiotapahtumalajit ja Karhulan Valimon tapauksessa myös Esmikko-lajit
            System.Collections.Generic.List<string> EmployeePayrollTypeList = new System.Collections.Generic.List<string>();
            string result = this.GetEmployeePayrollTypes(connection, fkEmployee, ref EmployeePayrollTypeList);

            //Käy läpi henkilön tapahtumalajit ja katso, esiintyykö detailissa
            string fkNewPayrollType = string.Empty;//Uuden rivin tapahtumalaji
            foreach(string fkEmployeePayrollType in EmployeePayrollTypeList)
            {
                bool found = false;
                foreach(string fkTransactionPayrollType in transactionPayrollTypeList)
                {//Rivin seuraava tapahtuma
                    if (fkTransactionPayrollType == fkEmployeePayrollType)
                    {//Löytyi riviltä, ei lisätä tätä
                        found = true;
                        break;
                    }
                }
                if(!found)
                {//Tätä tapahtumaa ei löytynyt riviltä
                    fkNewPayrollType = fkEmployeePayrollType;
                    break;
                }
            }

            if(!string.IsNullOrEmpty(fkNewPayrollType))
            {//Löytyi uusi tapahtumalaji, alusta rivi sen perusteella
                Infoglove.Pages.Maintain.SetComboSelectedItem(this.PayrollRow_PayrollType, fkNewPayrollType);
                result = this.GetPayrollTypeData(connection, fkNewPayrollType, dtPayrollRow, fkEmployee);
                if (Infoglove.Library.Method.IsError(result))
                {//Virhetilanne
                    this.master.ShowErrorMessage(result);
                }
            }
        }

        private string GetEmployeePayrollTypes(SqlConnection connection, string fkEmployee, ref System.Collections.Generic.List<string> EmployeePayrollTypeList)
        {//Hae palkansaajan vakiotapahtumalajit ja Karhulan Valimolla myös Esmikko-tapahtumalajit

            if(Infoglove.Session.Method.GetSessionObject(this.tbPageInstance.Text, "EmployeePayrollTypeList") != null)
            {//Löytyi sessiosta
                EmployeePayrollTypeList = (System.Collections.Generic.List<string>)Infoglove.Session.Method.GetSessionObject(this.tbPageInstance.Text, "EmployeePayrollTypeList");
                return string.Empty;
            }

            int maxPayrollTypeCount = 14;
            for (int i = 1; i <= maxPayrollTypeCount; i++)
            {//Henkilöllä voi olla maksimissaan 14 vakiopalkkalajia
                string fkPayrollType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "FKPayrollType" + i, fkEmployee);
                if (!string.IsNullOrEmpty(fkPayrollType))
                {//Palkkalaji i oli määritelty
                    if(!EmployeePayrollTypeList.Contains(fkPayrollType))
                        EmployeePayrollTypeList.Add(fkPayrollType);
                }
                else
                    break;//Aukkoja ei hyväksytä palkansaajan vakiopalkkalajeissa! Lajit siis loppuvat ensimmäiseen aukkoon.
            }
            
            if(Infoglove.Library.Method.IsCompanyLicence("karh01"))
            {//Hae Karhulan Esmikko-tuntikirjauksista erilliset palkkalajit ja lisää ne henkilön vakiopalkkalajien sekaan ennen ennakonpidätystä
                string aliasEmployee = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "AliasId", fkEmployee);
                string periodStartDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PeriodStartDate.Text);
                string periodEndDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PeriodEndDate.Text);
                string selectString = "SELECT DISTINCT AliasPayrollType FROM PayrollRaw WHERE AliasEmployee = '" + aliasEmployee + "' AND StartDate >= '" + periodStartDate + "' AND StartDate <= '" + periodEndDate + "' ORDER BY AliasPayrollType DESC";
                DataTable dtPayrollRaw = new DataTable();
                string result = Infoglove.Sql.Transaction.FillDataTable(connection, dtPayrollRaw, selectString);
                
                int withholdIndex = 0;//Ennakonpidätyslajin indeksi vakiopalkkalajien listassa
                foreach (string fkPayrollType in EmployeePayrollTypeList)
                {//Etsi henkilön palkkalajien listasta ennakonpidätys, sen tunnistaa siitä, että laji on 900 tai suurempi
                    string payrollType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "UniqId", fkPayrollType);
                    if(Infoglove.Strings.Compare.IsGreaterThanOrEqualTo(payrollType,"900"))
                    {//Tämä se on
                        break;
                    }
                    withholdIndex++;
                }

                //Hae henkilön henkilöryhmä
                string fkEmployeeGroup = this.GetFKEmployeeGroup(connection);


                //Karhulan Valimolla palkkalajilla voi olla useita aliastunnuksia pilkulla eroteltuna, tee tunnuksista taulukko, jotta tulinta helpottuisi
                //Hae palkkalajit, joilla aliasId esiintyy ja joissa henkilöryhmää ei ole määritelty tai se on sama kuin käsiteltävän henkilön henkilöryhmä
                selectString = "SELECT PrimaryKey, AliasId, FKEmployeeGroup FROM PayrollType WHERE AliasId IS NOT NULL AND (FKEmployeeGroup IS NULL OR FKEmployeeGroup = '" + fkEmployeeGroup + "')" ;
                DataTable dtPayrollTypeAlias = new DataTable();
                result = Infoglove.Sql.Transaction.FillDataTable(connection, dtPayrollTypeAlias, selectString);
                System.Collections.Generic.Dictionary<string, string> aliasDictionary = new System.Collections.Generic.Dictionary<string, string>();
                foreach(DataRow drPayrollTypeAlias in dtPayrollTypeAlias.Rows)
                {//Siirrä aliastunnukset taulukkoon, jos duplikaatteja esiintyy, niin ne vain ohitetaan
                    string fkPayrollType = Infoglove.AdoNet.DataRow.StringValue(drPayrollTypeAlias, "PrimaryKey");
                    string aliasId = Infoglove.AdoNet.DataRow.StringValue(drPayrollTypeAlias, "AliasId");
                    if(aliasId.Contains(","))
                    {//Useita aliastunnuksia pilkulla eroteltuna, välilyöntejä ei voi esiintyä, koska ne on siivottu palkkalajien ylläpidossa
                        string[] aliasList = Infoglove.Strings.Manipulate.Split(aliasId, ",");
                        foreach(string alias in aliasList)
                        {//Siirrä nämä
                            if(!aliasDictionary.ContainsKey(alias))
                            {//Lisää listaan
                                Infoglove.Sql.Dictionary.Add(aliasDictionary, alias, fkPayrollType);
                            }
                        }
                    }
                    else
                    {//Yksinäinen aliastunnus
                        if (!aliasDictionary.ContainsKey(aliasId))
                        {//Lisää listaan
                            Infoglove.Sql.Dictionary.Add(aliasDictionary, aliasId, fkPayrollType);
                        }
                    }
                }


                foreach(DataRow drPayrollRaw in dtPayrollRaw.Rows)
                {//Lisää Esmikon tuntikirjauksista löytyneet palkkalajit henkilön vakiopalkkalajien listaan, tässä yksi Infogloven palkkalaji voi vastata useaa Esmikon palkkalajia
                    string aliasPayrollType = Infoglove.AdoNet.DataRow.StringValue(drPayrollRaw, "AliasPayrollType");
                    if (!string.IsNullOrEmpty(aliasPayrollType))
                    {//Löytyi, milloinkahan tämä voi olla tyhjä - ei pitäisi koskaan
                        string fkPayrollType = Infoglove.Sql.Transaction.GetColumnByGivenColumn(connection, "PayrollType", "PrimaryKey", "AliasId", aliasPayrollType);
                        if (!string.IsNullOrEmpty(fkPayrollType))
                        {//Palkkalaji löytyi
                            if (!EmployeePayrollTypeList.Contains(fkPayrollType))
                            {//Palkkalajia ei ollut ennestään, lisää listaan indeksin kohdalle
                                EmployeePayrollTypeList.Insert(withholdIndex, fkPayrollType);
                            }
                        }
                        else
                        {//Palkkalaji ei löytynyt, näin voi käydä, jos aliastunnuksia on useampia pilkulla eroteltuna
                            //Like-ehto ei kuitenkaan tarjoa pomminvarmaa tapaa tunnistaa, koska voi olla palkkalajit 61231 ja 61231-1
                            selectString = "SELECT PrimaryKey, AliasId FROM PayrollType WHERE AliasId LIKE '%" + aliasPayrollType + "%'";
                            DataTable dtAliasId = new DataTable();
                            result = Infoglove.Sql.Transaction.FillDataTable(connection, dtAliasId, selectString);
                            foreach(DataRow drAliasId in dtAliasId.Rows)
                            {//Etsi oikea aliasrivi, siinä on väkisinkin pilkku eli luettelo, koska muutenhan se olisi jo löytynyt edellisessä haarassa
                                string aliasId = Infoglove.AdoNet.DataRow.StringValue(drAliasId, "AliasId");
                                if(aliasId.Contains(","))
                                {//Hyvä ehdokas, esimerkiksi 61263,61262
                                    aliasId = "," + aliasId + ","; //Esimerkiksi  ,61263,61262, tällä estetään alimerkkijonojen löytyminen, esim pelkkä 612
                                    if(aliasId.Contains(","+aliasPayrollType+","))
                                    {//Löytyi, tämä kelpaa
                                        fkPayrollType = Infoglove.AdoNet.DataRow.StringValue(drAliasId, "PrimaryKey");
                                        if (!EmployeePayrollTypeList.Contains(fkPayrollType))
                                        {//Palkkalajia ei ollut ennestään, lisää listaan indeksin kohdalle
                                            EmployeePayrollTypeList.Insert(withholdIndex, fkPayrollType);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            //Tallenna henkilön palkkalajien lista sessioon, huom! Tämä on tietenkin muillakin kuin Karhulan Valimolla
            Infoglove.Session.Method.SetSessionObject(this.tbPageInstance.Text, "EmployeePayrollTypeList", EmployeePayrollTypeList);

            return string.Empty;

        }

        private string CalculateCoefficient(SqlConnection connection, string formula, string quantity)
        {//Laske kerroin laskentakaavasta

            if (string.IsNullOrEmpty(formula))
                return formula;

            if (formula.StartsWith("*"))
                return formula;//Kommentti

            string numberFormula = formula;

            int dayCount = Infoglove.Numbers.Integers.StringToInteger(quantity);
            if (dayCount <= 0)
            {//Tästä tulee nollapalkka...onkohan tämäkään missään käytössä
                numberFormula = numberFormula.Replace("Coefficient_HolidayYearAvgHourlyWage", "0");
                return numberFormula;
            }

            if (formula.Contains("Coefficient_xxxxxx"))
            {//Kertoimien laskentakaavoja ei toistaiseksi ole eikä ehkä tulekaan


            }

            return formula;//Kelpaa sellaisenaan
        }
        
        private string CalculateCalcValue(SqlConnection connection,string formula,string fkEmployee,DataTable dtPayrollRow)
        {//Laske kaavan arvo kentässä calcValue, esim Employee_PeriodWage+Employee_FringeBenefit1/Employee_PeriodHourConstant

            if(formula.StartsWith("*"))
                return formula;//Kommentti

            //Etsi operandit, esim Payroll_TableWithholdIncome jne
            System.Collections.Generic.List<string> operands = new System.Collections.Generic.List<string>();
            string result = this.FindFormulaOperands(formula,operands);
            string numberFormula = formula;

            foreach(string operand in operands)
            {//Seuraava operandi
                if(operand.StartsWith("Employee_"))
                {//Henkilöoperandi, PrevQtrAvgHourlyWage on laskettava edellisen neljänneksen tuntipalkkatapahtumista
                    if (operand.EndsWith("_PrevQtrAvgHourlyWage"))
                    {//Edellisen neljänneksen keskituntiansio
                        string columnValue = Infoglove.Business.Payroll.GetPrevQtrAvgHourlyWage(connection, fkEmployee, this.Payroll_PaymentDate.Text);
                        numberFormula = numberFormula.Replace(operand, columnValue);
                    }
                    else if (operand.EndsWith("_HolidayYearDailyWage"))
                    {//Maksupäivää edeltävän lomanmääräytymisvuoden päiväpalkka tunti- tai jaksopalkasta
                        string fkPayrollPeriod = Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_PayrollPeriod);
                        string fkPayrollPeriodType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollPeriod", "FKPayrollPeriodType", fkPayrollPeriod);
                        string methodDescription = string.Empty;

                        //Etsi maksupäivää edeltävän lomanmääräytymisvuoden alku- ja loppupäivä
                        string paymentDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PaymentDate.Text);
                        string holidayYearStartDate = Infoglove.Business.PayrollHoliday.GetHolidayYearStartDate(connection, paymentDate, false);
                        string holidayYearEndDate = Infoglove.Business.PayrollHoliday.GetHolidayYearEndDate(connection, paymentDate, false);

                        string columnValue = Infoglove.Business.PayrollHoliday.GetHolidayYearDailyWage(connection, fkEmployee, holidayYearStartDate, holidayYearEndDate, fkPayrollPeriodType, this.PayrollRow_PayrollType.Text, ref methodDescription);
                        numberFormula = numberFormula.Replace(operand, columnValue);
                        this.Payroll_HolidayCalcDoc.Text = methodDescription;
                    }
                    else if (operand.EndsWith("_HolidayYearAvgHourlyWage"))
                    {//Lomanmääräytymisvuoden tuntipalkka maksupäivää edeltävälle lomanmääräytymisvuodelle
                        string fkPayrollPeriod = Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_PayrollPeriod);
                        string fkPayrollPeriodType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollPeriod", "FKPayrollPeriodType", fkPayrollPeriod);
                        string message = string.Empty;

                        //Etsi maksupäivää edeltävän lomanmääräytymisvuoden alku- ja loppupäivä
                        string paymentDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PaymentDate.Text);
                        string holidayYearStartDate = Infoglove.Business.PayrollHoliday.GetHolidayYearStartDate(connection, paymentDate, false);
                        string holidayYearEndDate = Infoglove.Business.PayrollHoliday.GetHolidayYearEndDate(connection, paymentDate, false);

                        string columnValue = Infoglove.Business.PayrollHoliday.GetHolidayYearAvgHourlyWage(connection, fkEmployee, holidayYearStartDate, holidayYearEndDate, ref message);
                        numberFormula = numberFormula.Replace(operand, columnValue);
                    }
                    else
                    {//Hae arvo henkilön takaa tietokannasta
                        string columnName = Infoglove.Strings.Substring.From(operand, "_");//Esim. PeriodWage/169
                        string selectString = "SELECT " + columnName + " FROM Employee WHERE PrimaryKey = " + fkEmployee;
                        string columnValue = Infoglove.Sql.Transaction.GetString(connection, selectString);
                        numberFormula = numberFormula.Replace(operand, columnValue);//Esim 1650,00/169

                    }
                }
                else if (operand.StartsWith("Payroll_"))
                {//Palkkatositteelta dataa
                    if (operand == "Payroll_TableWithholdIncome")
                    {//Taulukkopidätystulo
                        string columnValue = this.Payroll_TableWithholdIncome.Text;
                        if (string.IsNullOrEmpty(columnValue))
                            columnValue = "0.00";
                        numberFormula = numberFormula.Replace(operand, columnValue);
                    }
                    else if (operand == "Payroll_PercentWithholdIncome")
                    {//Prosenttipidätystulo
                        string columnValue = this.Payroll_PercentWithholdIncome.Text;
                        if (string.IsNullOrEmpty(columnValue))
                            columnValue = "0.00";
                        numberFormula = numberFormula.Replace(operand, columnValue);
                    }
                    else if (operand == "Payroll_PensionIncome")
                    {//TyEL-työtulo
                        string columnValue = this.Payroll_PensionIncome.Text;
                        if (string.IsNullOrEmpty(columnValue))
                            columnValue = "0.00";
                        numberFormula = numberFormula.Replace(operand, columnValue);
                    }
                    else if (operand == "Payroll_TelIncome")
                    {//TEL-työtulo, historiallinen
                        string columnValue = this.Payroll_TelIncome.Text;
                        if (string.IsNullOrEmpty(columnValue))
                            columnValue = "0.00";
                        numberFormula = numberFormula.Replace(operand, columnValue);
                    }
                    else if (operand == "Payroll_LelIncome")
                    {//Lel-työtulo, historiallinen
                        string columnValue = this.Payroll_LelIncome.Text;
                        if (string.IsNullOrEmpty(columnValue))
                            columnValue = "0.00";
                        numberFormula = numberFormula.Replace(operand, columnValue);
                    }
                    else if (operand == "Payroll_TaelIncome")
                    {//Tael-työtulo, historiallinen
                        string columnValue = this.Payroll_TaelIncome.Text;
                        if (string.IsNullOrEmpty(columnValue))
                            columnValue = "0.00";
                        numberFormula = numberFormula.Replace(operand, columnValue);
                    }
                    else if (operand == "Payroll_WorkHours")
                    {//Työtunnit, tarvitaan esimerkiksi pekkasiin
                        string columnValue = this.Payroll_WorkHours.Text;
                        if (string.IsNullOrEmpty(columnValue))
                            columnValue = "0.00";
                        numberFormula = numberFormula.Replace(operand, columnValue);
                    }
                }
            }

            //Laske kaavan arvo vakiotoiminnolla
            string formulaValue = Infoglove.Report.RepCalc.EvaluateExpression(numberFormula);
            formulaValue = Infoglove.Numbers.Decimals.FormatDecimal(formulaValue, "0.00");//Toivottavasti kaksi desimaalia riittää, tämä pyöristys lisätty 12.12.2017
            return formulaValue;

        }

        private string CheckTaxationDays()
        { //Jos pidätyksen alainen tulo on nolla, niin ei veropäiviä
            //Huom! Ei saa tarkistaa kun vasta pidätyksen yhteydessä
            if( this.CalculateWithholdIncome("Payroll_TableWithholdIncome+Payroll_PercentWithholdIncome") == 0m)
                this.Payroll_TaxDayCount.Text = string.Empty;
            return string.Empty;
        }


        private string CalculateQuantity(SqlConnection connection, string incomeField, string formula, string fkEmployee, DataTable dtDetail, string fkPayrollType, decimal withholdIncome)
        {//Laske määräkentän sisältö, yleensä pidätysprosentti

            if(string.IsNullOrEmpty(fkEmployee))
            {
                return "[ERROR] tositteelle ei ole tallennettu palkansaajaa";
            }

            if(string.IsNullOrEmpty(formula))
                return formula;

            if (formula.StartsWith("*"))
                return formula;//Kommentti

            string numberFormula = formula;

            string fkPayrollPeriod = Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_PayrollPeriod);

            //Maksupäivä
            string paymentDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PaymentDate.Text);
            string payroll_uniqId = this.Payroll_UniqId.Text;
            string payroll_TaxDayCount = this.Payroll_TaxDayCount.Text;

            //Veropäiviä ei saa olla, jos tulo on nollilla pidätystä laskettaessa
            if (formula.Contains("Withhold"))
                this.CheckTaxationDays();

            if (formula.Contains("Withhold_Cumulative"))
            {//Kumulatiivinen pidätys
                string methodDescription = string.Empty;
                //Prosentti voi olla kumulatiivisessa pidätyksessä kumman tahansa merkkinen, jätetään merkki sillensä
                string cumulativeWithholdPercent = Infoglove.Business.Payroll.CalculateCumulativeWithholdPercent(connection, fkEmployee, withholdIncome, fkPayrollPeriod, fkPayrollType, paymentDate, payroll_uniqId, payroll_TaxDayCount, ref methodDescription);
                if (Infoglove.Library.Method.IsError(cumulativeWithholdPercent))
                    return cumulativeWithholdPercent;

                formula = formula.Replace("Withhold_Cumulative", cumulativeWithholdPercent);

                this.Payroll_WithholdCalcDoc.Text = methodDescription;
                return formula;
            }
            else if (formula.Contains("Withhold_Stepwise"))
            {//Portaittainen pidätys
                //Käy läpi portaat ja laske palkkasummaa vastaava keskimääräinen prosentti
                string stepwisePercent = "-" + Infoglove.Business.Payroll.CalculateStepwiseWithholdPercent(connection, incomeField, fkEmployee, paymentDate, withholdIncome);//Ei ole koskaan luonnostaan negatiivinen
                formula = formula.Replace("Withhold_Stepwise", stepwisePercent);
                return formula;
            }
            else if (formula.Contains("Withhold_YearLimit"))
            {//Pidätys ylimenevältä osalta
                //Tässä lajissa on vuosituloraja, jonka ylittävästä osasta menee lisäprosentti, muuten perusprosentti
                string methodDescription = string.Empty;
                string withholdPercent = Infoglove.Business.Payroll.CalculateYearLimitWithholdPercent(connection, fkEmployee, withholdIncome, fkPayrollType, paymentDate, payroll_uniqId, ref methodDescription);//Tulee aina positiivisena
                if (Infoglove.Library.Method.IsError(withholdPercent))
                    return withholdPercent;
                withholdPercent = "-" + withholdPercent;//Lisää etumerkki
                formula = formula.Replace("Withhold_YearLimit", withholdPercent.ToString());
                this.Payroll_WithholdCalcDoc.Text = methodDescription;
                return formula;
            }
            else if (formula.Contains("Withhold_Periodical"))
            {//Palkkakausikohtainen pidätys, uutuutena Karh01:n toiveesta tehty veropäiviin reagointi
                string methodDescription = string.Empty;
                decimal taxDayCount = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_TaxDayCount.Text);
                string periodicalPercent = "-" + Infoglove.Business.Payroll.CalculatePeriodicalWithholdPercent(connection, taxDayCount, incomeField, fkEmployee, withholdIncome, fkPayrollPeriod, paymentDate, payroll_uniqId, ref methodDescription);
                this.Payroll_WithholdCalcDoc.Text = methodDescription;
                formula = formula.Replace("Withhold_Periodical", periodicalPercent.ToString());
                return formula;
            }
            else if (formula.Contains("Withhold_Total"))
            {//Pidätys henkilön kokonaisveroprosentin mukaan
                string methodDescription = "Pidätyksen laskenta:" + Infoglove.Library.Constant.crlfString;
                methodDescription += "Pidätysprosentti on palkansaajan 'Kokonaisprosentti'" + Infoglove.Library.Constant.crlfString;
                string totalPercent = "-" + Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "TotalPercent", fkEmployee);//Ei ole koskaan luonnostaan negatiivinen
                if(totalPercent == "-")
                {//
                    methodDescription += "VIRHE: Henkilöltä " + this.Payroll_Employee.Text + " puuttuu tieto kentästä 'Kokonaisprosentti', päivitä tieto ja suorita toiminto uudelleen!";
                    formula = string.Empty;
                }
                else
                {
                    formula = formula.Replace("Withhold_Total", totalPercent);
                }
                this.Payroll_WithholdCalcDoc.Text = methodDescription;
                return formula;
            }
            else if (formula.Contains("Employee_TyelPercent"))
            {//Tyel-prosentti, tulee henkilön syntymäpäivän mukaan
                string description = string.Empty;//Tähän voisi tulla, minkä perusteella prosentti laskettiin, esim TyEl 17-52
                string columnValue = Infoglove.Business.Payroll.GetTyelPercentEmployee(connection, fkEmployee, this.Payroll_PaymentDate.Text, ref description);
                formula = formula.Replace("Employee_TyelPercent", columnValue.ToString());
                this.PayrollRow_Description.Text = description;//Mahtaako toimia
                return formula;
            }
            else if (formula.Contains("Employee_HolidayDaysSummer"))
            {//Kesälomapäivien lukumäärä maksupäivää edeltävälle lomanmääräytymisvuodelle

                //Etsi maksupäivää edeltävän lomanmääräytymisvuoden alku- ja loppupäivä
                string holidayYearStartDate = Infoglove.Business.PayrollHoliday.GetHolidayYearStartDate(connection, paymentDate, false);
                string holidayYearEndDate = Infoglove.Business.PayrollHoliday.GetHolidayYearEndDate(connection, paymentDate, false);

                string columnValue = Infoglove.Business.PayrollHoliday.GetEmployeeSummerHolidayDays(connection, fkEmployee, holidayYearStartDate, holidayYearEndDate);
                formula = formula.Replace("Employee_HolidayDaysSummer", columnValue.ToString());
                return formula;
            }
            else if (formula.Contains("Employee_HolidayDaysWinter"))
            {//Talvilomapäivien lukumäärä maksupäivää edeltävälle lomanmääräytymisvuodelle

                //Etsi maksupäivää edeltävän lomanmääräytymisvuoden alku- ja loppupäivä
                string holidayYearStartDate = Infoglove.Business.PayrollHoliday.GetHolidayYearStartDate(connection, paymentDate, false);
                string holidayYearEndDate = Infoglove.Business.PayrollHoliday.GetHolidayYearEndDate(connection, paymentDate, false);

                string columnValue = Infoglove.Business.PayrollHoliday.GetEmployeeWinterHolidayDays(connection, fkEmployee, holidayYearStartDate,holidayYearEndDate);
                formula = formula.Replace("Employee_HolidayDaysWinter", columnValue.ToString());
                return formula;
            }
            else if (formula.Contains("Employee_"))
            {//Jokin henkilörekisterin kenttä, esimerkiksi TradeUnionPercent, hae arvo tietokannasta
                string columnName = Infoglove.Strings.Substring.From(formula, "_");//Esim. TradeUnionPercent
                if (columnName.StartsWith("-"))
                    columnName = columnName.Replace("-", string.Empty);
                string selectString = "SELECT " + columnName + " FROM Employee WHERE PrimaryKey = " + fkEmployee;
                string columnValue = Infoglove.Sql.Transaction.GetString(connection, selectString);
                string returnValue = formula.Replace("Employee_" + columnName, columnValue);
                return returnValue;
            }
            else if (formula.StartsWith("Payroll_"))
            {//Jokin palkkatositteen kenttä, esimerkiksi WorkHours,haettava näytöltä!
                if(formula == "Payroll_WorkHours")
                {//Henkilön työtunnit
                    return this.Payroll_WorkHours.Text;
                }
                else
                {//Tätä kenttää ei toistaiseksi tueta
                    return "[ERROR] - palkkatositteen kenttä " + formula + " ei ole tuettu laskennassa, ole hyvä ja ota yhteys tukeen";
                }
            }
            else if (formula.Contains("PayrollRaw_"))
            {//Ulkoisesta tiedonkeruujärjestelmästä saatu määrätieto, yleensä siis tunnit
                if(formula == "SUM(PayrollRaw_Quantity)")
                {//Tiedonkeruujärjestelmästä saatu tieto
                    string columnValue = this.GetPayrollRawQuantity(connection, fkEmployee, fkPayrollType, this.Payroll_PeriodStartDate.Text, this.Payroll_PeriodEndDate.Text);
                    return columnValue;

                }
            }

            return formula;//Kelpaa sellaisenaan
        }

        private string GetPayrollRawQuantity(SqlConnection connection, string fkEmployee, string fkPayrollType, string periodStartDate, string periodEndDate)
        {//Hae ulkoisesta tiedonkeruusta kerätyt tunnit

            periodStartDate = Infoglove.Dates.Convert.StringToDateId(periodStartDate);
            periodEndDate = Infoglove.Dates.Convert.StringToDateId(periodEndDate);

            string employee = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "UniqId", fkEmployee);
            string payrollType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "UniqId", fkPayrollType);
            string aliasId = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "AliasId", fkPayrollType);
            if (string.IsNullOrEmpty(aliasId))
                return "[ERROR] virhe palkkalajilla '" + payrollType + "', aliastunnus puuttuu palkkalajilta";

            string aliasEmployee = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "AliasId", fkEmployee);
            if (string.IsNullOrEmpty(aliasEmployee))
                return "[ERROR] tuntitietojen haku henkilölle " + employee+ " ja aikavälille " + periodStartDate + " - " + periodEndDate + " ei onnistu, henkilön aliastunnus puuttuu";

            //
            //SELECT SUM(Quantity) FROM PayrollRaw WHERE AliasEmployee = '104-KV' AND AliasPayrollType IN ('61263','61262') AND StartDate >= '2018-01-16' AND StartDate <= '2018-01-31'
            string aliasIdString = "('" + aliasId + "')";//Esim ('61263'), toimii IN - ehdossa
            if(aliasId.Contains(","))
            {//Useita aliastunnuksia pilkulla eroteltuna, muuta IN - ehdon vaatimaan muotoon
                string[] aliasIdList = Infoglove.Strings.Manipulate.Split(aliasId, ",");
                aliasIdString = string.Empty;
                foreach(string id in aliasIdList)
                {
                    if (!string.IsNullOrEmpty(aliasIdString))
                        aliasIdString += ",";
                    aliasIdString += "'" + id + "'";
                }
                aliasIdString = "(" + aliasIdString + ")";
            }

            string selectString = "SELECT SUM(Quantity) FROM PayrollRaw WHERE AliasEmployee = '" + aliasEmployee + "' AND AliasPayrollType IN " + aliasIdString + " AND StartDate >= '" + periodStartDate + "' AND StartDate <= '" + periodEndDate + "'";
            string quantity = Infoglove.Sql.Transaction.GetString(connection, selectString);
            return quantity;
        }

        private string GetAdjusterValue(SqlConnection connection, string fkPayrollType, string payrollAdjuster)
        {//Hae annetun palkkalajin annetun ohjaimen arvo
            string fkPayrollAdjuster = Infoglove.Sql.Transaction.GetColumnByUniqId(connection, "PayrollAdjuster", "PrimaryKey", "Withhold");//Pidätys
            string selectString = "SELECT AdjusterValue FROM PayrollTypeRow WHERE FKPayrollType = '" + fkPayrollType + "' AND FKPayrollAdjuster =  '" + fkPayrollAdjuster + "'";
            string adjusterValue = Infoglove.Sql.Transaction.GetString(connection, selectString);//Esim T
            return adjusterValue;
        }

        private string FindFormulaOperands(string formula, System.Collections.Generic.List<string> operandList)
        {//Formula on esimerkiksi (Employee_PeriodWage+Employee_FringeBenefit1)/Employee_PeriodHourConstant
            //Muuta operaattorit välilyönneiksi ja splittaa näin syntynyt merkkijono
            formula = formula.Replace("(", " ");
            formula = formula.Replace(")", " ");
            formula = formula.Replace("+", " ");
            formula = formula.Replace("-", " ");
            formula = formula.Replace("*", " ");
            formula = formula.Replace("/", " ");
            string[] operands = Infoglove.Strings.Manipulate.Split(formula, " ");
            foreach(string operand in operands )
            {
                if(!string.IsNullOrEmpty(operand.Trim()))
                {//On olemassa
                    if (!operandList.Contains(operand))
                        operandList.Add(operand);
                    else
                        return "[ERROR] in formula, operand '" + operand + "' exists more than one time";
                }
            }
            return string.Empty;
        }

        private void CallbackCalculate(string callbackParameter)
        {//Callbackilla suoritettava laskenta, esim. [RowValue][Calculate][0], 
            this.master.RestoreInputControl(this.PayrollRow_PrimaryKey);
            string rowPrimaryKey = this.PayrollRow_PrimaryKey.Text;

            if (callbackParameter.Contains("[Calculate][0]"))
            {//Laskenta
                this.ShowDetailGrid();
            }

            this.CallbackParameter.Text = string.Empty;
        }

        private void AdjustContainers()
        {//Aseta otsikkotekstit, alasvetovalikot, menut ja JavaScript - koodit
            string pageId = Infoglove.Library.Method.GetCurrentPageId();

            //Tab-sivujen otsikot
            this.ASPxPageControl1.TabPages[0].Text = Infoglove.Localization.Method.GetLocalPhrase(pageId + "_TabPageHeader0");
            this.ASPxPageControl1.TabPages[1].Text = Infoglove.Localization.Method.GetLocalPhrase(pageId + "_TabPageHeader1");
            this.ASPxPageControl1.TabPages[2].Text = Infoglove.Localization.Method.GetLocalPhrase(pageId + "_TabPageHeader2");

            //Gridin otsikko
            this.rp_PayrollRow.HeaderText = Infoglove.Localization.Method.GetLocalPhrase(pageId + "_PayrollRowHeader");

            //Täytä alasvetovalikot
            this.FillCombos();

            //Täytä masterin AJAX-hakukenttien nimet sisältävä kombo
            this.FillMasterSearchColumn();

            //PALKANLASKENNAN ERIKOISUUS, JOS TOSITTEELLA EI OLE RIVEJÄ, NIIN PIILOTETAAN Insert - menutoiminto
            string maintenanceMode = this.master.GetMaintenanceMode();

            string menuString = "Search,Insert,Save,Delete,Print,Activity,Help";
            if (maintenanceMode == "insert")
            {//Tositteen lisäys, jos sillä ei ole rivejä, niin turha näyttää Insert-toimintoa
                if(this.master.dtDetailEntity.Rows.Count == 0)
                {//Ei rivejä, ei insertiä
                    menuString = "Search,Save,Delete,Activity,Help";
                }
            }

            this.ShowMenu(menuString);

            this.ShowDetailButtons();

            //Aseta F7 ja F8-näppäimelle arvo, jolla saat edestakaisen selauksen toimimaan
            this.master.AdjustF7F8Search(this.MasterSearch, "GetMaster");

            //Kokeellinen toiminto F9 rivin hyvaksymiseksi
            this.master.AdjustF9Activity(this.PayrollRow_PayrollType, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_Account, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_CreditAccount, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_CostCenter, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_Project, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_WorkOrder, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_Quantity, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_Unit, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_Coefficient, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_CalcValue, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_RowValue, "AcceptDetail");
            this.master.AdjustF9Activity(this.PayrollRow_Description, "AcceptDetail");

            //Ohitetaan nämä automaattisesti, kun painetaan tabbia tapahtumalajikentässä
            this.PayrollRow_Account.TabIndex = 1000;
            this.PayrollRow_CreditAccount.TabIndex = 1001;
            this.PayrollRow_CostCenter.TabIndex = 1002;
            this.PayrollRow_Project.TabIndex = 1003;
            this.PayrollRow_WorkOrder.TabIndex = 1004;

        }

        private void ShowDetailButtons()
        {//Näytä toimintonappulat
            Infoglove.Utils.ShowButtons("InsertDetail,AcceptDetail,DeleteDetail,Upload,RefreshDocumentMap,DeleteDocument");
        }

        private void FillCombos()
        {//Hae Combojen data

            using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
            {
                connection.Open();

                Infoglove.Pages.Maintain.FillComboSource(connection, "PayrollPeriod");
                Infoglove.Pages.Maintain.FillComboSource(connection, "Employee");
                Infoglove.Pages.Maintain.FillComboSource(connection, "Language");
                Infoglove.Pages.Maintain.FillComboSource(connection, "PayrollType");
                Infoglove.Pages.Maintain.FillComboSource(connection, "Unit");

                Infoglove.Pages.Maintain.FillComboSource(connection, "PayrollDebetAccount");
                Infoglove.Pages.Maintain.FillComboSource(connection, "PayrollCreditAccount");
                
                Infoglove.Pages.Maintain.FillComboSource(connection, "CostCenter");
                Infoglove.Pages.Maintain.FillComboSource(connection, "Project");
                Infoglove.Pages.Maintain.FillComboSource(connection, "WorkOrder");
                Infoglove.Pages.Maintain.FillComboSource(connection, "PayrollSendingType");
            }
        }

        private void FillMasterSearchColumn()
        {//Täytä hakucombon kentät, columnList sisältää kenttänimet pilkulla eroteltuna
            string columnList = "Payroll_UniqId," + this.otherMasterSearchColumn;
            if (this.MasterSearchColumn.Items.Count > 0) return;
            string[] columnNames = columnList.Split(',');
            int index = 0;
            foreach (string columnName in columnNames)
            {//Lisää seuraava elementti
                string localizedName = Infoglove.Localization.Method.GetLocalPhrase(columnName);
                DevExpress.Web.ListEditItem listItem = new DevExpress.Web.ListEditItem(localizedName, columnName);
                this.MasterSearchColumn.Items.Add(listItem);
                if (index == 0) //Ensimmäinen elementti valituksi
                    this.MasterSearchColumn.SelectedItem = listItem;
                index++;
            }
        }

        private void ShowMenu(string master)
        {//Näytä päätoimintojen mastersivun alla sijaitseva menu
            Infoglove.MasterPages.SiteTemplate siteTemplate = (Infoglove.MasterPages.SiteTemplate)Master;
            DevExpress.Web.ASPxMenu operationMenu = siteTemplate.OperationMenu;

            if (master == "Save,Cancel" || master == "Save,Restore")
            {//Lisäys- ja poistomoodin menu
                Infoglove.Pages.Utils.ShowOperationMenu(operationMenu, master, false);//Ei opastuksia
                return;
            }

            //Tulostuksen alamenu
            string subItemList = "PayrollVerificate";
            Infoglove.Pages.Utils.CreateOperationMenuSubItems(operationMenu, "Print", subItemList);

            //Toimintojen alamenu
            subItemList = "RecalculatePayrollVerificate";
            if(this.HasWorkTimeTransactions())
            {//Esmikon lähtötiedot
                subItemList += ",ShowWorkTimeTransactions,CalculateTyly";//Näytä lähtötiedot
            }

            if(Infoglove.Library.Method.IsSuperUser())
            {//Superkäyttäjän toiminnot
                if (!string.IsNullOrEmpty(subItemList))
                    subItemList += ",";
                subItemList += "UpdatCostCentersFromEmployees";
            }

            Infoglove.Pages.Utils.CreateOperationMenuSubItems(operationMenu, "Activity", subItemList);


            Infoglove.Pages.Utils.ShowOperationMenu(operationMenu, master, true);//Mukana opastukset
        }

        protected void Page_Init(object sender, EventArgs e)
        {//Sivun initialisointi ja mastersivun alaisten toimintonäppäimen käsittelyeventin rekisteröinti
            Infoglove.MasterPages.SiteTemplate siteTemplate = (Infoglove.MasterPages.SiteTemplate)Master;
            siteTemplate.operationMenuClickedDelegate += new CommandEventHandler(OperationMenuClickedOnMasterPage);//Rekisteröi eventhandleri mastersivun buttonien käsittelyyn
            //this.MaintainPageControl1.buttonClickedDelegate += new CommandEventHandler(ButtonClickedOnToolbar);
        }

        private void OperationMenuClickedOnMasterPage(object sender, CommandEventArgs e)
        {//Mastersivun buttoneventtien käsittely
            string itemName = e.CommandName;
            string itemText = e.CommandArgument.ToString();
            this.ProcessMenuSelection(itemName);
        }

        private void ButtonClickedOnToolbar(object sender, CommandEventArgs e)
        {//Hakutoimintojen työkalupalkin buttonien klikkauseventtien käsittely
            string buttonId = e.CommandName;
            string itemText = e.CommandArgument.ToString();
            this.ProcessButtonClick(buttonId);
        }

        private void ShowModeMessage(bool changed)
        {//Näytä lisäyksessä tiedote käyttäjälle masterbuttonien palkkiin
            //Jos tieto on juuri muuttunut, niin ilmoita siitäkin
            PayrollRow_Grid.Settings.ShowGroupPanel = false;
            string maintenanceMode = this.master.GetMaintenanceMode();
            if (maintenanceMode == Infoglove.Library.Constant.modeInsert)
            {//Uuden pääentiteetin lisäys
                string generalTextNew = Infoglove.Localization.Method.GetLocalPhrase("GeneralTextNew");//Uusi
                string generalInsertNewMasterMessage = Infoglove.Localization.Method.GetLocalPhrase("GeneralInsertNewMasterMessage");//anna arvot ja tallenna
                string entityName_Payroll = Infoglove.Localization.Method.GetLocalPhrase("Payroll");
                this.master.ShowMessage(this.master.lblMaintenanceMessage, generalTextNew + " [" + entityName_Payroll + "], " + generalInsertNewMasterMessage);
                this.ShowMenu("Save,Cancel");//Lisäyksessä menuun näkyviin vain tallennus ja peruutus
                this.MasterSearch.Text = string.Empty;
            }
            else
            {//Muu kuin lisäys, poistetaan tiedote
                this.master.ShowMessage(this.master.lblMaintenanceMessage, string.Empty);
            }

            if (changed)
            {//Riviä muutettu, muistuta tallennuksesta
                this.master.ShowMessage(this.master.lblMaintenanceMessage, "General_SaveWarning");
            }
        }

        private void InitializePayrollCopy()
        {//Tee kopioinnin vaatimat alustukset
            this.Payroll_UniqId.Text = string.Empty;
            this.Payroll_PrimaryKey.Text = "-1";
            this.master.InitializeEntity(this.detailEntity);
            this.master.InitializeGridForCopy(this.detailEntity, this.masterEntity, this.PayrollRow_Grid);

        }

        private bool HasWorkTimeTransactions()
        {//Onko työaikatapahtumat käytössä
            if (Infoglove.Library.Method.IsCompanyLicence("karh01"))
                return true;
            return false;
        }

        private void ShowWorkTimeTransactions(SqlConnection connection)
        {//Näytä lähtötietoja näytöllä olevalle henkilölle tai kaikille henkilöille
            
            if (!this.HasWorkTimeTransactions())
                return;

            string selectString = "SELECT TOP 1000 TransactionType, AliasEmployee, ReasonCode, AliasPayrollType, StartDate, EndDate, Quantity, Description FROM PayrollRaw WHERE (1=1) ORDER BY StartDate DESC";
            string fkEmployee = this.GetFKEmployee(connection);
            string periodStartDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PeriodStartDate.Text);
            string periodEndDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PeriodEndDate.Text);
            string aliasEmployee = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "AliasId", fkEmployee);
            if(!string.IsNullOrEmpty(fkEmployee))
            {//Näytä vain tämän henkilön ja palkkakauden tiedot
                selectString = selectString.Replace("1=1", "AliasEmployee = '" + aliasEmployee + "' AND StartDate >='" + periodStartDate + "' AND EndDate <= '" + periodEndDate + "'");
            }
            DataTable dtDataSource = new DataTable();
            string result = Infoglove.Sql.Transaction.FillDataTable(connection, dtDataSource, selectString);
            Infoglove.Session.Method.SetSessionObject("GridDataSource_PayrollRaw", dtDataSource.DefaultView);
            this.PayrollRaw_Grid.Columns.Clear();
            this.PayrollRaw_Grid.DataBind();

            this.Div_DocumentMap.Visible = true;
            this.rp_DocumentMap.Visible = true;
        }

        private void InitializePayroll()
        {//Alusta palkkatosite

            string fkPayrollPeriod = string.Empty;

            //Käytä edellisen tallennetun tositteen palkkajaksoa, jos sellainen löytyy
            string fkPrevPayrollPeriod = Infoglove.Session.Method.GetSessionString("Prev_Payroll_PayrollPeriod");
            if (!string.IsNullOrEmpty(fkPrevPayrollPeriod))
            {//Edellisellä tositteella tallennettu palkkajakso on olemassa
                string prevPayrollPeriod = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(null, "PayrollPeriod", "UniqId", fkPrevPayrollPeriod);
                if (!string.IsNullOrEmpty(prevPayrollPeriod))
                {//Löytyi tietokannastakin
                    fkPayrollPeriod = fkPrevPayrollPeriod;
                }
            }

            if (!string.IsNullOrEmpty(fkPayrollPeriod))
            {//Palkkajakso löytyi
                Infoglove.Pages.Maintain.SetComboSelectedItem(this.Payroll_PayrollPeriod, fkPayrollPeriod);
            }

            //Alusta palkkakausitiedot
            using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
            {
                connection.Open();
                fkPayrollPeriod = Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_PayrollPeriod);
                this.GetPayrollPeriodData(connection, fkPayrollPeriod);
            }

        }

        private void CalculateTyly()
        {//Laske uudelleen ansaittu ja maksettu tyly palkkakauden alkupäivän vuoden kaikille tositteille

            //Hae kaikki tositteet palkkajakson alkupäivän vuodelta
            string periodStartDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PeriodStartDate.Text);
            if (string.IsNullOrEmpty(periodStartDate))
            {
                this.master.ShowErrorMessage("[ERROR] hae näytölle tosite, jonka tylytilaston haluat laskea uudelleen");
                return;
            }

            string periodEndDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PeriodEndDate.Text);

            using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
            {
                connection.Open();

                string fkEmployee = this.GetFKEmployee(connection);
                string paymentDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PaymentDate.Text);
                string fkPayroll = this.Payroll_PrimaryKey.Text;
                string result = Infoglove.Business.PayrollTyly.UpdateTylyStatistics(connection, fkPayroll, fkEmployee, paymentDate, periodStartDate, periodEndDate, true);

            }
            this.ShowMasterAndGrid(true);
        }

        private void UpdatCostCentersFromEmployees()
        {//Päivitä henkilöiden takaa kustannuspaikat tapahtumariveille, käytössä vain Karh01

            //Hae kaikki tositteet palkkajakson alkupäivän vuodelta
            string paymentDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PaymentDate.Text);
            if (string.IsNullOrEmpty(paymentDate))
            {
                this.master.ShowErrorMessage("[ERROR] hae näytölle tosite, jonka maksupäivän vuodelle haluat tehdä täsmäyksen!");
                return;
            }

            using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
            {
                connection.Open();

                int periodYear = Infoglove.Dates.Get.YearOfDateId(paymentDate);
                DataTable dtPayroll = new DataTable();
                string selectString = "SELECT PrimaryKey, FKEmployee FROM Payroll WHERE SUBSTRING(PeriodStartDate,1,4) = " + periodYear;
                string result = Infoglove.Sql.Transaction.FillDataTable(connection, dtPayroll, selectString);

                foreach (DataRow drPayroll in dtPayroll.Rows)
                {
                    string fkPayroll = Infoglove.AdoNet.DataRow.StringValue(drPayroll, "PrimaryKey");
                    string fkEmployee = Infoglove.AdoNet.DataRow.StringValue(drPayroll, "FKEmployee");
                    result = this.UpdateCostCenters(connection, fkPayroll, fkEmployee);

                }
            }
        }

        private bool IsAutomationCostRow(string rowNo)
        {//Onko tämä rivinumero automaattitallennettava kustannusrivi
            return rowNo == "994" || rowNo == "995" || rowNo == "996" || rowNo == "997" || rowNo == "999";
        }

        private string UpdateCostCenters(SqlConnection connection, string fkPayroll, string fkEmployee)
        {//Päivitä kustannuspaikat tälle palkkatositteelle, käytössä vain Karhulan Valimolla
            //Hae palkkatositteen tositerivit
            string selectString = "SELECT PrimaryKey, RowNo, FKCostCenter, CostCenter, FKAccount, Account, FKCreditAccount, CreditAccount FROM PayrollRow WHERE FKPayroll = " + fkPayroll;
            DataTable dtPayrollRow = new DataTable();
            string result = Infoglove.Sql.Transaction.FillDataTable(connection, dtPayrollRow, selectString);
            foreach(DataRow drPayrollRow in dtPayrollRow.Rows)
            {//Käsittele rivit
                string fkPayrollRow = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PrimaryKey");

                //Tarkista tilin tyyppi, kustannuspaikka tulee vain, jos kumpikin tili on tulostili
                string fkAccount = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "FKAccount");
                string fkAccountType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Account", "FKAccountType", fkAccount);
                string fkCreditAccount = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "FKCreditAccount");
                string fkCreditAccountType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Account", "FKAccountType", fkCreditAccount);
                string rowNo = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "RowNo"); ;
                
                System.Collections.Generic.Dictionary<string, string> payrollRowValues = new System.Collections.Generic.Dictionary<string, string>();
                Infoglove.Sql.Dictionary.Add(payrollRowValues, "PrimaryKey", fkPayrollRow);

                if (this.IsAutomationCostRow(rowNo) || (fkAccountType != "1" && fkCreditAccountType != "1"))
                {//Rivillä on kustannuspaikka, koska on automaattikustannusrivi tai kumpikaan tili ole tasetili
                    string fkCostCenter = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "FKCostCenter", fkEmployee);
                    string costCenter = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "CostCenter", fkCostCenter);
                    Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKCostCenter", fkCostCenter);
                    Infoglove.Sql.Dictionary.Add(payrollRowValues, "CostCenter", costCenter);
                    result = Infoglove.Sql.Transaction.Update(connection, "PayrollRow", payrollRowValues);
                }
                else
                {//Ei tule kustannuspaikkaa, tässä tapauksessa tyhjennetään kustannuspaikka!
                    string fkCostCenter = null;
                    string costCenter = null;
                    Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKCostCenter", fkCostCenter);
                    Infoglove.Sql.Dictionary.Add(payrollRowValues, "CostCenter", costCenter);
                    result = Infoglove.Sql.Transaction.Update(connection, "PayrollRow", payrollRowValues);
                }
            }

            return string.Empty;

        }

        private void ProcessMenuSelection(string operationId)
        {//Käsittele mastersivun menuvalinta
            if (operationId == "search")
            {//Hae pääentiteetin rivejä, esim. tilauksia, laskuja jne
                this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeSearch);
                this.master.SearchEntity(this.masterEntity, "", "");
            }
            else if (operationId == "calculatetyly")
            {//Laske tylytilasto näytön tositteelle
                this.CalculateTyly();
            }
            else if (operationId == "updatcostcentersfromemployees")
            {//Päivitä kustannuspaikat palkansaajien takaa
                this.UpdatCostCentersFromEmployees();
            }
            else if (operationId == "recalculatepayrollverificate")
            {//Päivitä tosite uudelleen laskenta-arvojen suhteen
                this.RecalculatePayrollVerificate();
            }
            else if (operationId == "newpayroll" || operationId == "insert")
            {//Lisää master
                this.master.InitializeEntity(this.masterEntity);
                this.master.InitializeEntity(this.detailEntity);
                this.master.InitializeGrid(this.detailEntity, this.masterEntity, this.PayrollRow_Grid);
                this.InitializePayroll();
                this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeInsert);
                this.ShowModeMessage(false);
                this.ASPxPageControl1.ActiveTabPage = this.ASPxPageControl1.TabPages[1];//Otsikon tietojen välilehti
                this.Payroll_Employee.Focus();
            }
            else if (operationId == "showworktimetransactions")
            {//Näytä palkanlaskennan lähtötiedot
                this.ShowMasterAndGrid(false);//Näytä tiedot
                this.ShowWorkTimeTransactions(null);
                this.ASPxPageControl1.ActiveTabPage = this.ASPxPageControl1.TabPages[2];
            }
            else if (operationId == "copypayroll")
            {//Lisää master käyttämällä näytöllä olevaa pohjana, ei käytössä tässä toiminnossa
                this.InitializePayrollCopy();
                this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeInsert);
                this.ShowModeMessage(false);
            }
            else if (operationId == "print" || operationId == "payrollverificate")
            {//Tulosta palkkatosite eli palkkapussi
                //TODO - palkkapussien tulostus on jossakin vaiheessa tehtävä eräajona
                string payrollVerificatePrintBatchPage_Active = Infoglove.Parameter.GetParameterElementValue("Payroll", "PayrollVerificatePrintBatchPage", "Active");
                if (string.IsNullOrEmpty(payrollVerificatePrintBatchPage_Active))
                    payrollVerificatePrintBatchPage_Active = "false";

                if (payrollVerificatePrintBatchPage_Active == "true")
                {//Tulosta palkkatosite tosite-erän tulostuksen kautta, TODO tekemättä
                    this.master.SetCurrentPagePrimaryKey(this.Payroll_PrimaryKey.Text);
                    this.master.ClearPageInstance(this.masterEntity, this.detailEntity);
                    this.PrintPayrollVerificateBatch();
                }
                else if (!string.IsNullOrEmpty(this.Payroll_UniqId.Text))
                {//Tulosta palkkatosite
                    this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeSearch);
                    this.master.StoreInputControls(this.masterEntity, this.detailEntity);

                    Infoglove.Session.Method.SetSessionObject("XtraReport_Payroll", null);
                    //string fkPayrollStatus = null;
                    string fkEmployee = this.GetFKEmployee(null);// Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_Employee);
                    Infoglove.Business.PrintPayroll.SetBatchConstraints(this.Payroll_UniqId.Text, null, null,null);

                    string query = Infoglove.Library.Method.GetCurrentPageRoot() + "Payroll/PayrollReport.aspx?callingpage=" + Infoglove.Library.Method.GetCurrentPagePath();

                    this.master.SetCurrentPagePrimaryKey(this.Payroll_PrimaryKey.Text);
                    this.master.ClearPageInstance(this.masterEntity, this.detailEntity);
                    this.master.RedirectTo(query);
                }
            }

            else if (operationId == "save")
            {//Tallenna lisäykset, muutokset ja poistot
                string primaryKey = this.Payroll_PrimaryKey.Text.ToLower();
                
                string result = this.SaveChanges();
                if (string.IsNullOrEmpty(result) && this.master.dtDetailEntity.Rows.Count == 0 && !primaryKey.Contains("deleted"))
                {//Kaikki onnistui, mutta tositteella ei ole rivejä, kohdistetaan rivien välilehdelle ja alustetaan rivi
                    this.ASPxPageControl1.ActiveTabPage = this.ASPxPageControl1.TabPages[0];//Tapahtumarivien välilehti
                    using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                    {
                        connection.Open();
                        this.master.InitializeEntity(this.detailEntity);
                        this.InitializePayrollRow(connection);
                    }
                    this.PayrollRow_PayrollType.Focus();
                }
                else
                {//Alkuperäinen käsittely
                    if (string.IsNullOrEmpty(result))
                    {//Kaikki onnistui ilman ilmoituksia, tee jatkokäsittely, esim. deletoinnin tapauksessa alustus ja sanomien häivytys
                        this.ShowMasterAndGrid(true);
                        if (primaryKey.IndexOf(Infoglove.Library.Constant.statusDeleted) > 0)
                            this.master.InitializePage(this.masterEntity, this.detailEntity, this.PayrollRow_Grid, this.MasterSearch);
                        this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeMaintain);
                        this.ShowModeMessage(false);
                    }
                    else
                    {//Näytä virheilmoitus
                        Infoglove.Library.Method.ProcessMessage(result, this.master.lblErrorMessage, this.master.lblMaintenanceMessage);
                    }
                }
            }
            else if (operationId == "cancel")
            {//Hylkää kaikki muutokset ja palaa alkutilanteeseen
                this.master.InitializePage(this.masterEntity, this.detailEntity, this.PayrollRow_Grid, this.MasterSearch);
                this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeMaintain);
            }
            else if (operationId == "delete")
            {//Merkitse kortti riveineen poistettavaksi
                this.master.RestoreDtDetailEntity(this.detailEntity);

                using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                {
                    connection.Open();
                    this.AcceptDetailChanges(connection, "deleteall");
                }

                this.Payroll_PrimaryKey.Text = this.Payroll_PrimaryKey.Text + "Deleted";
                this.ShowMasterAndGrid(false);//Näytä tiedot
                this.ShowMenu("Save,Restore");
                this.master.ShowMarkedDeletedMessage(this.master.lblMaintenanceMessage, this.masterEntity);
                this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeMaintain);
                Infoglove.Library.Method.WriteToPageLog("Payroll marked as deleted:" + this.Payroll_UniqId.Text);
            }
            else if (operationId.EndsWith("help"))
            {//Näytä opastus
                Infoglove.Library.Method.ShowHelp("Payroll", operationId);
            }
            else if (operationId == "restore")
            {//Palauta poistomerkintä kortilta ja riveiltä
                using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                {
                    connection.Open();
                    this.AcceptDetailChanges(connection, "restoreall");
                }

                this.Payroll_PrimaryKey.Text = Infoglove.Numbers.Integers.ForceToPositiveIntegerString(this.Payroll_PrimaryKey.Text);
                this.ShowMasterAndGrid(false);//Näytä tiedot
                this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeMaintain);
            }
            this.master.StoreInputControls(this.masterEntity, this.detailEntity);
        }

        private void PrintPayrollVerificateBatch()
        {//Tulosta palkkatositteet eränä
            this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeSearch);
            this.master.StoreInputControls(this.masterEntity, this.detailEntity);
            this.master.SetPagePrimaryKey(Infoglove.Library.Method.GetCurrentPageId(), this.Payroll_PrimaryKey.Text);

            Infoglove.Session.Method.SetSessionObject("XtraReport_PayrollVerificate", null);
            string query = Infoglove.Library.Method.GetCurrentPageRoot() + "Payroll/PrintPayrollVerificates.aspx?callingpage=" + Infoglove.Library.Method.GetCurrentPagePath() + "&payrollConstraint=" + this.Payroll_UniqId.Text;
            this.master.RedirectTo(query);
        }


        public void Button_Click(object sender, EventArgs e)
        {//Tämän sivun toimintonappuloiden klikkauseventin suoritus
            this.master.TestSession();
            this.ProcessButtonClick(((DevExpress.Web.ASPxButton)sender).ID);
        }

        private void ShowDetailGrid()
        {//Gridin viewstate on false, joten grid joudutaan lähes aina virkistämään postbackin tai callbackin yhteydessä
            this.master.ShowDetailGrid(this.detailEntity, this.masterEntity, this.Payroll_PrimaryKey.Text, this.PayrollRow_Grid);
        }

        private void ProcessButtonClick(string controlId)
        {//Prosessoi buttonin painallus
            if (controlId.StartsWith("Search"))
            {//Hae pääentiteetti annetulla hakukriteerillä
                this.SearchMasterEntity(controlId);
            }
        }

        private void ProcessSearchRequest()
        {//Käsittele hakuun liittyvät kutsuparametrit ja suorita sen vaatimat haut

            this.ClearEmployeePayrollTypeList();

            string queryString = this.Request.QueryString.ToString();
            if (!this.master.PreprocessSearchRequest(this.Payroll_PrimaryKey, ref queryString)) return;//Ei tarvita jatkokäsittelyjä

            if (Infoglove.Strings.Find.QueryParameter(queryString, this.masterEntity + "_PrimaryKey") != null)
            {//Kortin otsikko

                this.Payroll_PrimaryKey.Text = Infoglove.Strings.Find.QueryParameter(queryString, this.masterEntity + "_PrimaryKey");
                string forceSearch = Infoglove.Strings.Find.QueryParameter(queryString, "forcesearch");
                if (forceSearch == "true")
                    this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeSearch);
                string maintenanceMode = this.master.GetMaintenanceMode();
                if (maintenanceMode == Infoglove.Library.Constant.modeMaintain || maintenanceMode == Infoglove.Library.Constant.modeInsert)
                {//Ylläpitotoiminto kesken
                    this.ShowMasterAndGrid(false);//Ei haeta kannasta, ylläpito kesken
                    this.master.SetPagePrimaryKey(Infoglove.Library.Method.GetCurrentPageId(), this.Payroll_PrimaryKey.Text);
                    return;//Ei tallenneta kontrolleja 11.4.2009 alkaen!
                }
                else
                {//Pakotettu haku tietokannasta
                    this.ShowMasterAndGrid(true);
                    this.master.SetPagePrimaryKey(Infoglove.Library.Method.GetCurrentPageId(), this.Payroll_PrimaryKey.Text);
                    if (this.Request.QueryString.ToString().Contains("restore")) //Jos restore, niin siirryttävä ylläpitomoodiin, muuten näytöllä tehdyt
                        this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeMaintain);//muutokset tallentuvat vasta toisella yrityksellä
                    this.ShowWorkTimeTransactions(null);

                }
            }
            else if (Infoglove.Strings.Find.QueryParameter(queryString, this.detailEntity + "_PrimaryKey") != null)
            {//Tapahtuman rivi haettu linkkikentän avulla
                this.ShowMasterAndGrid(true);//ei toimi enää
            }

            this.master.StoreInputControls(this.masterEntity, this.detailEntity);
        }

        private bool ShowMasterAndGrid(bool forceDatabaseSearch)
        {//Näytä master ja grid. Jos forceDatabaseSearch=true, niin hae tiedot tietokannasta
            if (forceDatabaseSearch)
            {//Haetaan tiedot kannasta

                string result = Infoglove.DataAccess.GetMasterEntity(Infoglove.Pages.Instance.GetPageInstance(this.tbPageInstance), this.masterEntity, this.Payroll_PrimaryKey.Text);
                if (result == "-1") return false;

                Infoglove.Pages.Utils.StoreEntityDocumentIdToSession(null, this.masterEntity, this.Payroll_UniqId.Text);

                if (Infoglove.Library.Method.IsError(this.master.ProcessMessage(result))) return false;
                this.master.ClearDtDetail(this.detailEntity);//Erittäin tärkeä, muuten gridin haku ei hae uuden kortin rivejä
                this.master.InitializeEntity(this.detailEntity, this.masterEntity);//Alusta, ettei näy vanhaa tietoa
            }

            //Aseta masterin hakukenttä valitun hakukentän nimen mukaan
            string selectedValue = this.MasterSearchColumn.SelectedItem.Value.ToString();
            if (selectedValue == this.otherMasterSearchColumn)
                this.MasterSearch.Text = this.Payroll_Employee.Text;
            else
                this.MasterSearch.Text = this.Payroll_UniqId.Text;

            this.master.ShowDetailGrid(this.detailEntity, this.masterEntity, this.Payroll_PrimaryKey.Text, this.PayrollRow_Grid);//Hae detailin rivikokoelma gridiin

            //HUOM! Tallenna nimikkeen primääriavain muistiin popupikkunaa varten
            this.master.SetPagePrimaryKey(Infoglove.Library.Method.GetCurrentPageId(), this.Payroll_PrimaryKey.Text);

            return true;
        }

        protected bool AcceptDetailChanges(System.Data.SqlClient.SqlConnection connection, string mode)
        {//Sijoita detaljitapahtuma datatauluihin ja gridille

            string fkPayrollRow = this.master.GetStoredControlText(this.PayrollRow_PrimaryKey);//rivin primääriavain persistenttitaulusta
            if (fkPayrollRow.Contains("Inserted") && mode != "restoreall")
                return true;//Tämä rivi oli jo lisätty

            //Laske uudelleen rivin kokonaisarvo
            decimal quantity = Infoglove.Numbers.Decimals.StringToDecimal(this.PayrollRow_Quantity.Text);
            decimal calcValue = Infoglove.Numbers.Decimals.StringToDecimal(this.PayrollRow_CalcValue.Text);
            decimal coefficient = Infoglove.Numbers.Decimals.StringToDecimal(this.PayrollRow_Coefficient.Text);
            string fkPayrollType = Infoglove.Pages.Maintain.GetComboSelectedValue(this.PayrollRow_PayrollType);
            string quantityFormula = this.PayrollRow_QuantityFormula.Text;
            string fkEmployee = this.GetFKEmployee(connection);// Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_Employee);
            decimal rowValue = Infoglove.Business.Payroll.CalculateRowValue(connection, quantity, ref calcValue, coefficient, quantityFormula, fkEmployee, fkPayrollType);
            this.PayrollRow_RowValue.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(rowValue);
            this.PayrollRow_CalcValue.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(calcValue);

            string errorMessage = string.Empty;

            //Tarkista, että ainakin toinen tili on annettu
            string fkAccount = Infoglove.Pages.Maintain.GetComboSelectedValue(this.PayrollRow_Account);
            if (fkAccount == "0") fkAccount = string.Empty;
            string fkCreditAccount = Infoglove.Pages.Maintain.GetComboSelectedValue(this.PayrollRow_CreditAccount);
            if (fkCreditAccount == "0") fkCreditAccount = string.Empty;

            if (!this.master.AcceptDetailEntity(connection, this.detailEntity, this.masterEntity, fkPayrollRow, mode, ref errorMessage))
            {//Validointi epäonnistui
                this.master.ShowMessage(this.master.lblMaintenanceMessage, errorMessage);
                return false;
            }
            this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeMaintain);
            this.ShowModeMessage(true);//Anna varoitus tallennustarpeesta

            //Rivin primääriavaimeen on tallennettava alustustieto, jotta tämä metodi ei missään tilanteessa tallentaisi sitä uudelleen
            //Tilannehan tulee vastaan silloin, kun browseri lähettää jonossa kaksi palvelupyyntöä, esim. määrämuutoskäsittely ja hyväksyntä samanaikaisesti
            this.PayrollRow_PrimaryKey.Text = Infoglove.Library.Constant.newRowPrimaryKey + "Inserted";
            this.master.StoreInputControl(this.PayrollRow_PrimaryKey);

            //Laske otsikolle summakenttien arvo
            bool newRowInserted = fkPayrollRow.ToLower().Contains("inserted");
            this.CalculateTotals(connection, newRowInserted, false);

            return true;
        }


        private string RecalculatePayrollVerificate()
        {//Laske tosite uudelleen laskenta-arvojen suhteen
            string result = string.Empty;
            using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
            {
                connection.Open();
                result = this.CalculateTotals(connection, false, true);
                this.ShowDetailGrid();
            }
            return result;
        }

        private string CalculateTotals(SqlConnection connection, bool newRowInserted, bool forceRecalcAll)
        {//Laske arvot tapahtumaotsikolle kaikkien rivien perusteella

            DataTable dtPayrollRow = Infoglove.DataAccess.RestoreDtDetailEntity(Infoglove.Pages.Instance.GetPageInstance(this.tbPageInstance), this.detailEntity);

            //Lasketaan kaikki otsikon kentät alusta alkaen
            decimal tableWithholdIncome = 0;
            decimal percentWithholdIncome = 0;
            decimal allowance = 0;
            decimal costCompensation = 0;
            decimal fringeBenefit = 0;
            decimal moneyWage = 0;

            decimal employeePensionFee = 0;
            decimal unemplInsuranceFee = 0;
            
            decimal otherAllowance = 0;
            decimal taxWithhold = 0;
            decimal pensionIncome = 0;
            decimal telIncome = 0;
            decimal lelIncome = 0;
            decimal taelIncome = 0;
            decimal sotuIncome = 0;
            decimal totalHours = 0;
            decimal tradeUnionFee = 0;
            string fkEmployee = this.GetFKEmployee(connection);// Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_Employee);

            foreach (DataRow drPayrollRow in dtPayrollRow.Rows)
            {//Poista automaattirivien data
                string payrollType = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PayrollRow_PayrollType");
                if(Infoglove.Strings.Compare.IsGreaterThan(payrollType,"990"))
                {//Poista automaattirivin data, automaattirivejä ei kuitenkaan tarvitse poistaa!
                    Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_CalcValue", string.Empty);
                    Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_Coefficient", string.Empty);
                    Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_Quantity", string.Empty);
                    Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_RowValue", string.Empty);
                }
            }

            foreach (DataRow drPayrollRow in dtPayrollRow.Rows)
            {//Laske kaikkien rivien vaikutus
                if (!Infoglove.Pages.Instance.DetailRowIsDeleted(drPayrollRow))
                {//Hae rivin tapahtumalajin ohjainarvot
                    string fkPayrollType = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PayrollRow_FKPayrollType");
                    string payrollType = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PayrollRow_PayrollType");

                    //Lue ohjainarvot suoraan palkkatapahtumalta
                    string adjusterValues = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "AdjusterValues", fkPayrollType);
                    string[] adjusterList = Infoglove.Strings.Manipulate.Split(adjusterValues, ";");

                    decimal quantity = Infoglove.AdoNet.DataRow.DecimalValueNotNull(drPayrollRow, "PayrollRow_Quantity");
                    decimal coefficient = Infoglove.AdoNet.DataRow.DecimalValueNotNull(drPayrollRow, "PayrollRow_Coefficient");
                    decimal calcValue = Infoglove.AdoNet.DataRow.DecimalValueNotNull(drPayrollRow, "PayrollRow_CalcValue");
                    string quantityFormula = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PayrollRow_QuantityFormula");

                    decimal rowValue = Infoglove.Business.Payroll.CalculateRowValue(connection, quantity, ref calcValue, coefficient, quantityFormula, fkEmployee, fkPayrollType);
                    Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_RowValue", rowValue);
                    Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_CalcValue", calcValue);

                    foreach (string adjusterTag in adjusterList)
                    {//Seuraava ohjainarvo
                        if (adjusterTag == "TableWithholdIncome")
                        {//Taulukkopidätyksen alainen tulo
                            tableWithholdIncome += rowValue;
                        }
                        else if (adjusterTag == "PercentWithholdIncome")
                        {//Prosenttipidätyksen alainen tulo
                            percentWithholdIncome += rowValue;
                        }
                        else if (adjusterTag == "Absence")
                        {//Poissaolo, päivitettävä poissaolopäivät ja poissaolotunnit palkkakauden ensimmäiselle kuukaudelle
                            //Näitä ei saa tapahtumariviltä, eli on päivitettävä käsin!
                        }
                        else if (adjusterTag == "Allowance")
                        {//Vähennys
                            allowance += rowValue;
                        }
                        else if (adjusterTag == "AutomaticMoneyWage")
                        {//Automaattinen rahapalkka, tämä taitaa olla parasta tehdä vasta tallennuksen yhteydessä

                        }
                        else if (adjusterTag == "AutomaticSocialSecurityFee")
                        {//Automaattinen sotu, tämä taitaa olla parasta tehdä vasta tallennuksen yhteydessä

                        }
                        else if (adjusterTag == "CostCompensation")
                        {//Kustannuskorvaus
                            costCompensation += rowValue;
                        }
                        else if (adjusterTag == "FringeBenefit")
                        {//Luontoisetu
                            fringeBenefit += rowValue;
                        }
                        else if (adjusterTag == "MoneyWage")
                        {//Rahapalkka
                            moneyWage += rowValue;
                        }
                        else if (adjusterTag == "UnemplInsuranceFee")
                        {//Työttömyysvakuutusmaksu
                            unemplInsuranceFee += rowValue;
                        }
                        else if (adjusterTag == "EmployeePensionFee")
                        {//Työntekijän eläkemaksu
                            employeePensionFee += rowValue;
                        }
                        else if (adjusterTag == "OtherAllowance")
                        {//Muu vähennys
                            otherAllowance += rowValue;
                        }
                        else if (adjusterTag == "TaxWithhold")
                        {//Muu vähennys
                            taxWithhold += rowValue;
                        }
                        else if (adjusterTag == "SotuIncome")
                        {//Sotulaskennan pohjatulo
                            sotuIncome += rowValue;
                        }
                        else if (adjusterTag == "PensionIncome")
                        {//TyEl-eläkeansio
                            pensionIncome += rowValue;
                        }
                        else if (adjusterTag == "TelIncome")
                        {//TEL-ansio, historiallinen
                            telIncome += rowValue;
                        }
                        else if (adjusterTag == "LelIncome")
                        {//Lel-tilastoon, historiallinen
                            lelIncome += rowValue;
                        }
                        else if (adjusterTag == "TaelIncome")
                        {//TaEl-tilastoon, historiallinen
                            taelIncome += rowValue;
                        }

                        else if (adjusterTag == "WorkHours")
                        {//Työtunteihin
                            totalHours += quantity;
                        }
                        else if (adjusterTag == "TradeUnionFee")
                        {//AY-maksu
                            tradeUnionFee += rowValue;
                        }
                    }
                }

            }

            //Sijoita kontrolleihin
            this.Payroll_MoneyWage.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(moneyWage, ',', "0.00");
            this.Payroll_FringeBenefit.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(fringeBenefit, ',', "0.00");
            this.Payroll_Allowance.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(allowance, ',', "0.00");
            this.Payroll_CostCompensation.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(costCompensation, ',', "0.00");
            this.Payroll_TableWithholdIncome.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(tableWithholdIncome, ',', "0.00");
            this.Payroll_PercentWithholdIncome.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(percentWithholdIncome, ',', "0.00");
            this.Payroll_TaxWithhold.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(taxWithhold, ',', "0.00");
            this.Payroll_TradeUnionFee.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(tradeUnionFee, ',', "0.00");
            this.Payroll_OtherAllowance.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(otherAllowance, ',', "0.00");
            this.Payroll_EmployeePensionFee.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(employeePensionFee, ',', "0.00");
            this.Payroll_UnemplInsuranceFee.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(unemplInsuranceFee, ',', "0.00");
            this.Payroll_PensionIncome.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(pensionIncome, ',', "0.00");
            this.Payroll_LelIncome.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(lelIncome, ',', "0.00");
            this.Payroll_TaelIncome.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(taelIncome, ',', "0.00");
            this.Payroll_SotuIncome.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(sotuIncome, ',', "0.00");

            //Työtunnit, jos kuitenkin lomakertymätunteihin on jo jotakin tallennettu, niin ei muuteta sitä enää
            this.Payroll_WorkHours.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(totalHours, ',', "0.00");
            if (string.IsNullOrEmpty(this.Payroll_HolidayAccHoursMonth1.Text))
                this.Payroll_HolidayAccHoursMonth1.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(totalHours, ',', "0.00");

            //Laske rahasuoritus palkkatapahtumaotsikolle uudelleen
            this.CalculatePaymentValue();

            foreach (DataRow drPayrollRow in dtPayrollRow.Rows)
            {//Tarkista, onko riveillä laskenta-arvo muuttunut, vaikuttaa erityisesti ennakonpidätykseen, mutta miksi ei muuhunkin?
                string calcValueFormula = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PayrollRow_CalcValueFormula");
                string payrollType = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PayrollRow_PayrollType");//Vain testiin

                //Tässä laskenta tehdään vain ennakonpidätyksen näkökulmasta! Muuhun ei ole toistaiseksi ollut tarvetta
                if (calcValueFormula.Contains("Payroll_TableWithholdIncome") || calcValueFormula.Contains("Payroll_PercentWithholdIncome"))
                {//Kaavassa on pidätyksen alaista tuloa tai pakotetaan kaikkien uudelleenlaskenta
                    decimal calcValue = Infoglove.AdoNet.DataRow.DecimalValueNotNull(drPayrollRow, "PayrollRow_CalcValue");
                    decimal withholdIncome = this.CalculateWithholdIncome(calcValueFormula);
                    if (calcValue != withholdIncome)
                    {//Pidätyksenalaista tuloa on muutettu
                        //Tämä lisätty 3.4.2018
                        decimal recalcValue = calcValue;
                        if (forceRecalcAll)
                            recalcValue = withholdIncome;

                        //    Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_CalcValue", withholdIncome);
                        //else
                        //    Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_CalcValue", calcValue);

                        decimal quantity = Infoglove.AdoNet.DataRow.DecimalValueNotNull(drPayrollRow, "PayrollRow_Quantity");
                        decimal coefficient = Infoglove.AdoNet.DataRow.DecimalValueNotNull(drPayrollRow, "PayrollRow_Coefficient");
                        string quantityFormula = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PayrollRow_QuantityFormula");

                        string fkPayrollType = Infoglove.AdoNet.DataRow.StringValue(drPayrollRow, "PayrollRow_FKPayrollType");

                        //Laske määräkenttä uudelleen
                        string withholdIncomeField = this.GetWithholdIncomeField(calcValueFormula);//Esim TableWithholdIncome+PercentWithholdIncome, tarvitaan seuraavassa metodissa
                        string quantity_ = this.CalculateQuantity(connection, withholdIncomeField, quantityFormula, fkEmployee, dtPayrollRow, fkPayrollType, recalcValue);
                        quantity = Infoglove.Numbers.Decimals.StringToDecimal(quantity_);
                        Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_Quantity", quantity);

                        decimal rowValue = Infoglove.Business.Payroll.CalculateRowValue(connection, quantity, ref recalcValue, coefficient, quantityFormula, fkEmployee, fkPayrollType);
                        Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_CalcValue", recalcValue);
                        Infoglove.AdoNet.DataRow.StoreToCell(drPayrollRow, "PayrollRow_RowValue", rowValue);

                    }

                }

            }
            
            return string.Empty;

        }

        private void CalculatePaymentValue()
        {//Laske palkkatapahtumaotsikon kontrolleista rahapalkka uudlleen
            decimal moneyWage = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_MoneyWage.Text);
            decimal fringeBenefit = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_FringeBenefit.Text);
            decimal allowance = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_Allowance.Text);
            decimal costCompensation = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_CostCompensation.Text);
            decimal taxWithhold = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_TaxWithhold.Text);
            decimal tradeUnionFee = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_TradeUnionFee.Text);
            decimal otherAllowance = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_OtherAllowance.Text);
            decimal employeePensionFee = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_EmployeePensionFee.Text);
            decimal unemplInsuranceFee = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_UnemplInsuranceFee.Text);
            decimal paymentValue = moneyWage + costCompensation + taxWithhold + tradeUnionFee + unemplInsuranceFee + employeePensionFee + otherAllowance;
            this.Payroll_PaymentValue.Text = Infoglove.Numbers.Decimals.ForceToDecimalString(paymentValue, ',', "0.00");

        }

        private string PreprocessMaster(System.Data.SqlClient.SqlConnection connection)
        {//Esikäsittele masterentiteetti
            string paymentDate_Original = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PaymentDate.Text);
            if (!string.IsNullOrEmpty(this.Payroll_UniqId.Text))
            {//Tositenumero olemassa, tosite on tietokannassa, maksupäivä sieltä
                paymentDate_Original = Infoglove.Sql.Transaction.GetColumnByUniqId(connection, "Payroll", "PaymentDate", this.Payroll_UniqId.Text);
            }
            if (Infoglove.Business.Accounting.RegistrationPeriodClosed(connection, paymentDate_Original, "payroll"))
            {//Kausi suljettu, ei saa muuttaa
                return "[ERROR] LocalizedMessage=Accounting_PayrollRegistrationPeriodClosed;#0=" + paymentDate_Original;
            }

            this.CalculatePaymentValue();//Laske rahasuoritus uudelleen
            return string.Empty;
        }

        private string SaveChanges()
        {//Tallenna muutokset

            string result = string.Empty;
            string maintenanceMode = this.master.GetMaintenanceMode();
            if (maintenanceMode == Infoglove.Library.Constant.modeNone)
                return string.Empty;//Ei ylläpitotoimintaa


            //Tämä lienee turha ehto - ei ole muuallakaan
            //if(maintenanceMode != Infoglove.Library.Constant.modeInsert && string.IsNullOrEmpty(this.Payroll_UniqId.Text))
            //{//Yritys tallentaa tyhjä tunnus muussa kuin lisäysmoodissa
            //    string message = Infoglove.Localization.Method.TranslateMessage("[ERROR] LocalizedMessage:General_EntityUniqIdMissing");
            //    return message;
            //}

            //Päivitä tietokantaan kaikki muutokset
            bool newMaster = (this.Payroll_PrimaryKey.Text == Infoglove.Library.Constant.newRowPrimaryKey);
            result = this.DatabaseTransaction();
            if (Infoglove.Library.Method.IsError(result))
            {//Transaktio epäonnistui
                if (newMaster)
                {//Uuden lisäyksessä on ehdottomasti palautettava tositenumero ja primääriavain alkuperäiseen tilaansa
                    this.Payroll_UniqId.Text = string.Empty;
                    this.Payroll_PrimaryKey.Text = Infoglove.Library.Constant.newRowPrimaryKey;
                }
                this.master.ProcessMessage(result);
                this.ShowDetailGrid();
                return result;
            }

            //Combojen nollaus
            Infoglove.Session.Method.SetSessionObject("ComboDataSource_" + Infoglove.Library.Method.GetCurrentPageId(), null);

            return string.Empty;

        }
        
        protected string DatabaseTransaction()
        {//Samaan transaktioon detailentiteetin logiikka ja päivitys, masterentiteetin logiikka ja päivitys,loppulogiikka
            string transactionResult = string.Empty;
            this.master.dtDetailEntity = Infoglove.DataAccess.RestoreDtDetailEntity(Infoglove.Pages.Instance.GetPageInstance(this.tbPageInstance), detailEntity);

            System.Transactions.TransactionScopeOption transactionScopeOption = System.Transactions.TransactionScopeOption.Required;
            System.Transactions.TransactionOptions transactionOptions = new System.Transactions.TransactionOptions();
            transactionOptions.IsolationLevel = System.Transactions.IsolationLevel.ReadUncommitted;
            transactionOptions.Timeout = new TimeSpan(0, 10, 0);

            string payrollRowAutoAccept = Infoglove.Parameter.GetParameterValue("Payroll", "PayrollRowAutoAccept");

            try
            {
                using (System.Transactions.TransactionScope transactionScope = new System.Transactions.TransactionScope(transactionScopeOption, transactionOptions))
                {
                    using (System.Data.SqlClient.SqlConnection connection = new SqlConnection(Infoglove.Session.Property.ccString))
                    {
                        connection.Open();

                        string result = this.PreprocessMaster(connection);
                        if (Infoglove.Library.Method.IsError(result))
                        {//Virhe otsikon esikäsittelyssä
                            return result;
                        }

                        if (this.Payroll_PrimaryKey.Text == Infoglove.Library.Constant.newRowPrimaryKey)//Uusi masterentiteetin rivi
                        {//Uusi masterentiteetti eli palkkatosite, tallennus tapahtuu omana transaktionaan kirjastometodissa
                            transactionResult = Infoglove.DataAccess.InsertMasterEntity(connection, Infoglove.Pages.Instance.GetPageInstance(this.tbPageInstance), this.masterEntity);
                            if (Infoglove.Library.Method.IsError(transactionResult))
                                return transactionResult;
                            this.Payroll_PrimaryKey.Text = transactionResult;
                        }


                        if (bool.Parse(payrollRowAutoAccept))
                        {//Rivin hyväksyminen lennossa
                            if (this.master.HasRequiredFields(this.detailEntity))
                            {//On pakollisia kenttiä, tämän jälkeen kaikkien pakollisten kenttien on oltava ok, että acceptia kannattaa yrittää tässä
                                if (this.master.ValidateEntity(this.detailEntity))
                                    if (!this.AcceptDetailChanges(connection, string.Empty))
                                    {//Ei mennyt läpi
                                        this.ShowDetailGrid();
                                        return "[ERROR]";
                                    }
                            }
                        }

                        transactionResult = this.DetailLogic(connection, this.master.dtDetailEntity);
                        if (Infoglove.Library.Method.IsError(transactionResult))
                            return transactionResult;


                        transactionResult = Infoglove.DataAccess.PerformUpdateDetailEntity(connection, this.master.dtDetailEntity, this.detailEntity, this.masterEntity, this.Payroll_PrimaryKey.Text, null);
                        if (Infoglove.Library.Method.IsError(transactionResult))
                            return transactionResult;

                        transactionResult = this.MasterLogic(connection);
                        if (Infoglove.Library.Method.IsError(transactionResult))
                            return transactionResult;

                        transactionResult = Infoglove.DataAccess.PerformUpdateMasterEntity(connection, Infoglove.Pages.Instance.GetPageInstance(this.tbPageInstance), this.masterEntity, this.Payroll_PrimaryKey.Text);
                        if (Infoglove.Library.Method.IsError(transactionResult))
                            return transactionResult;

                        transactionResult = this.PostLogic(connection, this.master.dtDetailEntity);//Täydennyslogiikka
                        if (Infoglove.Library.Method.IsError(transactionResult))
                            return transactionResult;

                        transactionScope.Complete();
                    }
                }
            }
            catch (Exception transactionException)
            {//Transaktio kaatui
                string errorMessage = "[ERROR], transaction exception in DatabaseTransaction:" + transactionException.Message;
                Infoglove.Library.Method.WriteToPageLog(errorMessage);
                return errorMessage;
            }

            return transactionResult;

        }

        private string MasterLogic(System.Data.SqlClient.SqlConnection connection)
        {//Masterin logiikka
            return string.Empty;
        }

        private string DetailLogic(System.Data.SqlClient.SqlConnection connection, DataTable dtDetail)
        {//Detailrivien logiikka

            bool isSuperUser = Infoglove.Library.Method.IsSuperUser();

            int firstNegativeRowNo = 0;
            int lastPositiveRowNo = 0;
            int nextFreeRowNo = 0;

            string fkEmployee = this.GetFKEmployee(connection);
            foreach (DataRow drDetail in dtDetail.Rows)
            {//Päivitä rivien debet- ja kreditarvot, superkäyttäjä hakee toistaiseksi automaattisesti tilit palkkalajilta!

                string fkAccount = Infoglove.AdoNet.DataRow.StringValue(drDetail, "PayrollRow_FKAccount");
                string fkCreditAccount = Infoglove.AdoNet.DataRow.StringValue(drDetail, "PayrollRow_FKCreditAccount");
                int rowNo = Infoglove.AdoNet.DataRow.IntValue(drDetail, "PayrollRow_RowNo");

                //if(isSuperUser)
                //{//Toistaiseksi superkäyttäjä päivittää tilit kantaan tapahtumalajilta, kunnes kaikkien kannat ovat taas kunnossa
                //    string fkPayrollType = Infoglove.AdoNet.DataRow.StringValue(drDetail, "PayrollRow_FKPayrollType");
                //    fkAccount = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKAccount", fkPayrollType);
                //    fkCreditAccount = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKCreditAccount", fkPayrollType);
                //    string account = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "Account", fkAccount);
                //    string creditAccount = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "Account", fkCreditAccount);
                //    Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_FKAccount", fkAccount);
                //    Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_Account", account);
                //    Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_FKCreditAccount", fkCreditAccount);
                //    Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CreditAccount", creditAccount);
                //}

                if (Infoglove.Library.Method.IsCompanyLicence("karh01"))
                {//Kustannuspaikka tulee riville, jos kumpikaan rivin tili ei ole tasetili
                    string fkCostCenter = null;
                    string costCenter = null;
                    string fkAccountType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Account", "FKAccountType", fkAccount);
                    string fkCreditAccountType = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Account", "FKAccountType", fkCreditAccount);
                    if (fkAccountType != "1" && fkCreditAccountType != "1")
                    {//Kumpikaan ei ole tasetili, tulee kustannuspaikka
                        fkCostCenter = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "FKCostCenter", fkEmployee);
                        costCenter = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "CostCenter", fkCostCenter);
                    }
                    Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_FKCostCenter", fkCostCenter);
                    Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CostCenter", costCenter);
                
                }

                decimal rowValue = Infoglove.AdoNet.DataRow.DecimalValueNotNull(drDetail, "PayrollRow_RowValue");
                if(rowValue < 0)
                {//Ensimmäinen rivi, jolla on negatiivinen arvo
                    if (firstNegativeRowNo == 0)
                    {//Puuttuu vielä
                        firstNegativeRowNo = rowNo;
                        nextFreeRowNo = lastPositiveRowNo + 1;
                    }
                }
                else if (firstNegativeRowNo == 0)
                {//Pidätysrivi ei ole vielä tullut vastaan
                    lastPositiveRowNo = rowNo;
                }

                //Korjaa rivinumero tarvittaessa
                if (rowValue > 0 && firstNegativeRowNo > 0 && rowNo < 900)
                {//Rivejä lisätty vähennysrivien perään, muuta rivinumero
                    Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_RowNo", nextFreeRowNo);
                    nextFreeRowNo++;
                }
                
                decimal? nullDecimal = null;
                string nullString = null;

                //Päivitä DebetHc ja CreditHc kentän rowValue perusteella, automaattirivit jätetään myöhemmäksi
                if(rowNo < 990)
                {//Ei ole automaattirivi
                    if (!string.IsNullOrEmpty(fkAccount) && !string.IsNullOrEmpty(fkCreditAccount))
                    {//Molemmat tilit määritelty
                        //Menköön molemmat sellaisenaan debetiin ja kreditiin, pitäisiköhän merkit vaihtaa, jos rowValue < 0
                        Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_DebetHc", rowValue);
                        Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CreditHc", rowValue);
                    }
                    else if (!string.IsNullOrEmpty(fkAccount) && string.IsNullOrEmpty(fkCreditAccount))
                    {//Vain debet-tili määritelty

                        if (rowValue < 0)
                        {//rowValue on miinusmerkkinen, eli oikeasti on kyseessä kredit-tapahtuma

                            //Vaihdetaan tili kreditpuolelle mitään kyselemättä!

                            //Ota debet-tili talteen
                            fkAccount = Infoglove.AdoNet.DataRow.StringValue(drDetail, "PayrollRow_FKAccount");
                            string account = Infoglove.AdoNet.DataRow.StringValue(drDetail, "PayrollRow_Account");

                            //Tallenna debet-tili kredit-tilin paikalle ja nollaa debet-tili
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_FKCreditAccount", fkAccount);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CreditAccount", account);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_FKAccount", nullString);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_Account", nullString);

                            //Menee kreditpuolelle vastakkais- eli plusmerkkisenä
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_DebetHc", nullDecimal);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CreditHc", -rowValue);

                        }
                        else
                        {//Normaali debet-tapahtuma, sellaisenaan kredit-puolelle
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_DebetHc", rowValue);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CreditHc", nullDecimal);
                        }

                    }
                    else if (string.IsNullOrEmpty(fkAccount) && !string.IsNullOrEmpty(fkCreditAccount))
                    {//Vain kredit-tili määritelty, huom! Menee vastakkaismerkkisenä tässä tapauksessa
                        if (rowValue > 0)
                        {//Tässä on virhetilanne, jos rowValue on plusmerkkinen
                            
                            //Vaihdetaan tili debetpuolelle mitään kyselemättä!

                            //Ota kredit-tili talteen
                            fkCreditAccount = Infoglove.AdoNet.DataRow.StringValue(drDetail, "PayrollRow_FKCreditAccount");
                            string creditAccount = Infoglove.AdoNet.DataRow.StringValue(drDetail, "PayrollRow_CreditAccount");

                            //Tallenna kredit-tili debet-tilin paikalle ja nollaa kredit tili
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_FKAccount", fkCreditAccount);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_Account", creditAccount);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_FKCreditAccount", nullString);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CreditAccount", nullString);

                            //Tallenna debetpuolelle selleisenaan eli tietenkin plusmerkkisenä
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_DebetHc", rowValue);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CreditHc", nullDecimal);

                        }
                        else
                        {//Normaali kredit-tapahtuma, tallenna kreditiin vastakkaismerkkisenä
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_DebetHc", nullDecimal);
                            Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CreditHc", -rowValue);
                        }
                    }
                    else if (string.IsNullOrEmpty(fkAccount) && string.IsNullOrEmpty(fkCreditAccount))
                    {//Kumpikaan tili ei ole määritelty, parametriohjatusti joko hyväksytään tai ei
                        string checkRowAccount = Infoglove.Parameter.GetParameterValue("Payroll", "CheckRowAccount");
                        if(checkRowAccount == "true")
                            return "[ERROR] on row " + rowNo + ", no accounts. Please add an account and retry.";

                        //Tiliä ei tarkisteta, nulliarvot kenttiin. Näin toimii esimerkiksi Rokepe
                        Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_DebetHc", nullDecimal);
                        Infoglove.AdoNet.DataRow.StoreToCell(drDetail, "PayrollRow_CreditHc", nullDecimal);
                    }
                    else
                    {//Virhe logiikassa, näin ei voi tapahtua
                        return "[ERROR] in account adjusting method on row " + rowNo + ", please contact support!";
                    }

                }
            }

            return string.Empty;
        }

        private string PostLogic(System.Data.SqlClient.SqlConnection connection, DataTable dtDetail)
        {//Viimeiseksi suoritettava logiikka

            string fkPayroll = this.Payroll_PrimaryKey.Text;
            if (fkPayroll.ToLower().Contains("deleted"))
                return string.Empty;//Palkkatosite poistettu

            if (dtDetail.Rows.Count == 0)
            {//Rivejä ei vielä ole
                return string.Empty;
            }

            string result = string.Empty;
            foreach (DataRow drDetail in dtDetail.Rows)
            {//Poista rivit, joiden arvo ja määrä on nolla lukuun ottamatta pidätysrivejä, poista myös kaikki automaattirivit, koska ne luodaan joka tapauksessa uudelleen
                decimal rowValue = Infoglove.AdoNet.DataRow.DecimalValueNotNull(drDetail, "PayrollRow_RowValue");
                decimal quantity = Infoglove.AdoNet.DataRow.DecimalValueNotNull(drDetail, "PayrollRow_Quantity");
                string quantityFormula = Infoglove.AdoNet.DataRow.StringValue(drDetail, "PayrollRow_QuantityFormula");
                int rowNo = Infoglove.AdoNet.DataRow.IntValue(drDetail, "PayrollRow_RowNo");
                if ((rowValue == 0.00m && quantity == 0.00m) || rowNo > 990)
                {//Rivi on arvoltaan ja määrältään nolla
                    if(!quantityFormula.Contains("Withhold") )
                    {//Ei ole pidätysrivi tai on automaattirivi
                        string deleteString = "DELETE FROM PayrollRow WHERE FKPayroll = '" + fkPayroll + "' AND RowNo = '" + rowNo + "'";
                        result = Infoglove.Sql.Transaction.Execute(connection, deleteString);
                    }
                }
            }

            //Lisää rahapalkka ja sairausvakuutusmaksun osuus sosiaaliturvamaksusta; Karhulalla lisäksi Sosiaalikulut, Tapaturmavakuutus, Työttömyysvakuutus ja Ryhmähenkivakuutus
            string payrollType_MoneyWage = Infoglove.Parameter.GetParameterElementValue("Payroll","AutoPayrollType","MoneyWage");
            string payrollType_HealthInsurance = Infoglove.Parameter.GetParameterElementValue("Payroll", "AutoPayrollType", "HealthInsurance");

            //Tarkista, että rahapalkan palkkalaji on olemassa
            string fkPayrollType_AutoMoneyWage = Infoglove.Sql.Transaction.GetColumnByUniqId(connection, "PayrollType", "PrimaryKey", payrollType_MoneyWage);
            if (string.IsNullOrEmpty(fkPayrollType_AutoMoneyWage))
                return "[ERROR] in parameter, the automatic money wage payroll type '" + payrollType_MoneyWage + "' does not exist. Please change parameter Payroll/AutoPayrollType or insert a new Payroll Type";

            //Tarkista, että sotun palkkalaji on olemassa
            string fkPayrollType_HealthInsurance = Infoglove.Sql.Transaction.GetColumnByUniqId(connection, "PayrollType", "PrimaryKey", payrollType_HealthInsurance);
            if (string.IsNullOrEmpty(fkPayrollType_HealthInsurance))
                return "[ERROR] in parameter, the automatic social security fee payroll type '" + payrollType_HealthInsurance + "' does not exist. Please change parameter Payroll/AutoPayrollType or insert a new Payroll Type";

            string fkEmployee = this.GetFKEmployee(connection);
            string paymentDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PaymentDate.Text);

            //Etsi syntymäpäivä sotun sisältä, tarvitaan automaattilajeihin
            string socialSecurityNumber = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "SocialSecurityNumber", fkEmployee);
            string birthDate_= Infoglove.Dates.Get.BirthDateFromSocialSecurityNumber(socialSecurityNumber);
            DateTime birthDate = Infoglove.Dates.Convert.DateIdToDateTime(birthDate_);

            if (Infoglove.Library.Method.IsCompanyLicence("karh01"))
            {//Karhulan automaattilajit, eli UnemploymentInsurance=994;GroupLifeInsurance=995; AccidentInsurance=996; WorkPension=997

                string payrollType_WorkPension = Infoglove.Parameter.GetParameterElementValue("Payroll", "AutoPayrollType", "WorkPension");
                string payrollType_AccidentInsurance = Infoglove.Parameter.GetParameterElementValue("Payroll", "AutoPayrollType", "AccidentInsurance");
                string payrollType_GroupLifeInsurance = Infoglove.Parameter.GetParameterElementValue("Payroll", "AutoPayrollType", "GroupLifeInsurance");
                string payrollType_UnemploymentInsurance = Infoglove.Parameter.GetParameterElementValue("Payroll", "AutoPayrollType", "UnemploymentInsurance");

                decimal quantity = 0m;
                decimal calcValue = 0m;
                if (Infoglove.Business.Payroll.HasEmployeeTyel(connection, fkEmployee, paymentDate, birthDate))
                {//Työntekijä on TyEl:in alainen
                    quantity = Infoglove.Business.Payroll.GetTyElPercentGeneral(connection, paymentDate);//Vuonna 2018 tämä on 25.32%
                    calcValue = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_SotuIncome.Text);//Työeläke lasketaan varmaan samasta summasta kuin sairausvakuutusmaksu
                    result = this.UpdateAutomationRow(connection, dtDetail, fkPayroll, payrollType_WorkPension, quantity, calcValue);
                    if (Infoglove.Library.Method.IsError(result))
                        return result;
                }

                //Tapaturmavakuutus, aivan kaikilla
                quantity = Infoglove.Business.Payroll.GetAccidentInsurancePercent(connection, paymentDate);//Vuonn 2018 tämä on 1.2%
                calcValue = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_SotuIncome.Text);//Lasketaan varmaan tästä summasta
                result = this.UpdateAutomationRow(connection, dtDetail, fkPayroll, payrollType_AccidentInsurance, quantity, calcValue);
                if (Infoglove.Library.Method.IsError(result))
                    return result;


                if (Infoglove.Business.Payroll.HasUnemploymentInsurance(connection, fkEmployee, paymentDate, birthDate))
                {//Työntekijällä on työttömyysvakuutus
                    quantity = Infoglove.Business.Payroll.GetUnemploymentInsurancePercent(connection, paymentDate);//Vuonn 2018 tämä on 2.889%
                    calcValue = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_SotuIncome.Text);//Työttömyysvakuutuskin lasketaan varmaan tästä
                    result = this.UpdateAutomationRow(connection, dtDetail, fkPayroll, payrollType_UnemploymentInsurance, quantity, calcValue);
                    if (Infoglove.Library.Method.IsError(result))
                        return result;
                }

                //Ryhmähenkivakuutus, aivan kaikilla
                quantity = Infoglove.Business.Payroll.GetGroupLifeInsurancePercent(connection, paymentDate);//Vuonn 2018 tämä on 0.066%
                calcValue = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_SotuIncome.Text);//Lasketaan varmaan tästä summasta
                result = this.UpdateAutomationRow(connection, dtDetail, fkPayroll, payrollType_GroupLifeInsurance, quantity, calcValue);
                if (Infoglove.Library.Method.IsError(result))
                    return result;

            }

            //Rahapalkan tiliöinti, tietenkin kaikilla
            result = this.UpdateAutoMoneyWageRow(connection, dtDetail, fkPayroll, fkPayrollType_AutoMoneyWage, payrollType_MoneyWage);
            if (Infoglove.Library.Method.IsError(result))
                return result;


            if (Infoglove.Business.Payroll.HasHealthInsurance(connection, fkEmployee, paymentDate, birthDate))
            {//Sairausvakuutusrivi
                result = this.UpdateHealthInsuranceRow(connection, dtDetail, fkPayroll, fkPayrollType_HealthInsurance, payrollType_HealthInsurance);
                if (Infoglove.Library.Method.IsError(result))
                    return result;
            }

            //Päivitä tositeotsikon tilastokentät rivien mukaisiksi
            result = this.UpdatePayrollStatisticsColumns(connection, this.Payroll_UniqId.Text);
            if (Infoglove.Library.Method.IsError(result))
                return result;
            
            if (Infoglove.Library.Method.IsCompanyLicence("karh01"))
            {//Karhulan Valimon lomapalkkavaraus- ja tylylaskenta
                string periodStartDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PeriodStartDate.Text);
                string periodEndDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PeriodEndDate.Text);
                result = Infoglove.Business.PayrollTyly.UpdateTylyStatistics(connection, fkPayroll, fkEmployee, paymentDate, periodStartDate, periodEndDate, false);
                if (Infoglove.Library.Method.IsError(result))
                    return result;

                result = Infoglove.Business.PayrollHoliday.UpdateHolidayStatistics(connection, fkPayroll, fkEmployee, paymentDate);
                if (Infoglove.Library.Method.IsError(result))
                    return result;

            }

            //Näytä lähtötietoja
            this.ShowWorkTimeTransactions(connection);

            //Edellinen palkkajakso talteen
            string fkPayrollPeriod = Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_PayrollPeriod);
            Infoglove.Session.Method.SetSessionObject("Prev_Payroll_PayrollPeriod", fkPayrollPeriod);

            //Rivien uudelleennumerointi
            string selectString = "SELECT PrimaryKey,RowNo FROM PayrollRow WHERE FKPayroll = " + fkPayroll + " AND RowNo < 900 ORDER BY RowNo";
            DataTable dtRenumber = new DataTable();
            result = Infoglove.Sql.Transaction.FillDataTable(connection, dtRenumber, selectString);
            int newRowNo = 10;
            foreach(DataRow drRenumber in dtRenumber.Rows)
            {
                System.Collections.Generic.Dictionary<string, string> payrollRowValues = new System.Collections.Generic.Dictionary<string, string>();
                string fkPayrollRow = Infoglove.AdoNet.DataRow.StringValue(drRenumber,"PrimaryKey");
                Infoglove.Sql.Dictionary.Add(payrollRowValues, "PrimaryKey", fkPayrollRow);
                Infoglove.Sql.Dictionary.Add(payrollRowValues, "RowNo", newRowNo);
                result = Infoglove.Sql.Transaction.Update(connection, "PayrollRow", payrollRowValues);
                newRowNo = newRowNo + 10;
            }

            
            return string.Empty;

            //Tyhjennä sessiosta henkilön vakiopalkkalajit
            //this.ClearEmployeePayrollTypeList();

        }

        
        private string UpdateAutoMoneyWageRow(SqlConnection connection, DataTable dtPayrollRow, string fkPayroll, string fkPayrollType, string payrollType)
        {//Lisää tai päivitä automaattinen rahapalkkarivi

            System.Collections.Generic.Dictionary<string, string> payrollRowValues = new System.Collections.Generic.Dictionary<string, string>();

            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKPayroll", fkPayroll);
            int rowNo = Infoglove.Numbers.Integers.StringToInteger(payrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "RowNo", rowNo);

            //Palkkalaji
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKPayrollType", fkPayrollType);
            payrollType = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "PayrollType", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "PayrollType", payrollType);

            //Debet-tili, aina nulli
            string fkAccount = null;// Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKAccount", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKAccount", fkAccount);
            string account = null;// Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "Account", fkAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Account", account);

            //Kredit-tili
            string fkCreditAccount = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKCreditAccount", fkPayrollType);
            string creditAccount = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "Account", fkCreditAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKCreditAccount", fkCreditAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "CreditAccount", creditAccount);

            //Quantity on tässä miinus yksi
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Quantity", "-1");

            //Yksikkökin lisätään
            string fkUnit = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKUnit", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKUnit", fkUnit);
            string unit = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Unit", "UniqId", fkUnit);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Unit", unit);

            Infoglove.Sql.Dictionary.Add(payrollRowValues, "CalcValue", this.Payroll_PaymentValue.Text);

            //Kerroin on vakio
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Coefficient", "1");

            //RowValue - ei tarvitse laskea
            decimal paymentValue = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_PaymentValue.Text);
            if (paymentValue == 0m)
                return string.Empty;//Ei lisätä mitään

            Infoglove.Sql.Dictionary.Add(payrollRowValues, "RowValue", -paymentValue);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "DebetHc", "0");
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "CreditHc", paymentValue);//Vain kredit-määrä tarvitaan

            //Description palkkalajilta
            string description = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "Description", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Description", description);

            string result = Infoglove.Sql.Transaction.Insert(connection, "PayrollRow", payrollRowValues);
            if(result == "0")
            {//Ei mennyt, päivitä
                string selectString = "SELECT PrimaryKey FROM PayrollRow WHERE RowNo=" + rowNo + " AND FKPayroll = " + fkPayroll;
                string fkPayrollRow = Infoglove.Sql.Transaction.GetString(connection, selectString);
                if(!string.IsNullOrEmpty(fkPayrollRow))
                {
                    Infoglove.Sql.Dictionary.Add(payrollRowValues, "PrimaryKey", fkPayrollRow);
                    result = Infoglove.Sql.Transaction.Update(connection, "PayrollRow", payrollRowValues);
                }
                else
                {//Ei päivittynyt
                    return "[ERROR] trying to insert automatic money wage row, please contact support";
                }
            }

            return result;

        }

        private string UpdateHealthInsuranceRow(SqlConnection connection, DataTable dtPayrollRow, string fkPayroll, string fkPayrollType, string payrollType)
        {//Lisää sairausvakuutuksen tiliöintirivi

            System.Collections.Generic.Dictionary<string, string> payrollRowValues = new System.Collections.Generic.Dictionary<string, string>();

            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKPayroll", fkPayroll);
            int rowNo = Infoglove.Numbers.Integers.StringToInteger(payrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "RowNo", rowNo);

            //Palkkalaji
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKPayrollType", fkPayrollType);
            payrollType = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "PayrollType", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "PayrollType", payrollType);

            //Lisää tilit, debet-puolella tili otetaan ensisijaisesti palkansaajan sotutilistä (kenttä Employee.SotuAccount) ja toissijaisesti parametrista, joka määräytyy henkilön sotukoodista
            string fkEmployee = this.GetFKEmployee(connection);// Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_Employee);
            string fkAccount = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "FKSotuAccount", fkEmployee);
            if(string.IsNullOrEmpty(fkAccount))
            {//Henkilökohtaista sotutiliä ei ole määritelty, käytä sotukoodia
                string ssnCode = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "SsnCode", fkEmployee);
                if(string.IsNullOrEmpty(ssnCode))
                {//Sotukoodi puuttuu, huono juttu
                    return "[ERROR] palkansaajalta puuttuu sekä sotutili että sotukoodi. Tallenna henkilölle mieluiten sotutili (SotuAccount) tai ainakin sotukoodi (SsnCode) 1 tai 2.";
                }
                string ssnParameterKey = "Ssn" + ssnCode;
                string ssnAccount = Infoglove.Parameter.GetParameterElementValue("Payroll", "AutoSotuAccount", ssnParameterKey);
                fkAccount = Infoglove.Sql.Transaction.GetColumnByUniqId(connection, "Account", "PrimaryKey", ssnAccount);
                if (string.IsNullOrEmpty(fkAccount))
                {//Tiliä ei ole
                    return "[ERROR] automaattinen sosiaaliturvan debet-tili ei löytynyt henkilön sotukoodilla '" + ssnCode + "', ota yhteys tukeen";
                }
            }
            
            string account = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "Account", fkAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKAccount", fkAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Account", account);

            //Kredittili otetaan palkkalajilta
            string fkCreditAccount = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKCreditAccount", fkPayrollType);
            string creditAccount = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "Account", fkCreditAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKCreditAccount", fkCreditAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "CreditAccount", creditAccount);

            if (Infoglove.Library.Method.IsCompanyLicence("karh01"))
            {//Kustannuspaikka riville
                string fkCostCenter = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "FKCostCenter", fkEmployee);
                string costCenter = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "CostCenter", fkCostCenter);
                Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKCostCenter", fkCostCenter);
                Infoglove.Sql.Dictionary.Add(payrollRowValues, "CostCenter", costCenter);
            }

            //HealthInsurancePercent parametrista, AutoSotuPercent on poistunut
            decimal quantity = Infoglove.Business.Payroll.GetHealthInsurancePercent(connection, this.Payroll_PaymentDate.Text);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Quantity", quantity);

            //Yksikkö lienee prosentti
            string fkUnit = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKUnit", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKUnit", fkUnit);
            string unit = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Unit", "UniqId", fkUnit);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Unit", unit);

            //CalcValue on Sotu_Income
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "CalcValue", this.Payroll_SotuIncome.Text);

            //Kertoimen avulla lasketaan prosentti
            decimal coefficient = 0.01m;
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Coefficient", coefficient);

            decimal calcValue = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_SotuIncome.Text);
            decimal rowValue = quantity * calcValue * coefficient;

            if (rowValue == 0m)
                return string.Empty;//Ei lisätä mitään

            //RowValue
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "RowValue", Infoglove.Numbers.Decimals.ForceToDecimalString(rowValue,',',"0.00"));

            //Debet ja Kredit menevät saman merkkisinä, niin kumoavat sopivasti toisensa!
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "DebetHc", Infoglove.Numbers.Decimals.ForceToDecimalString(rowValue, ',', "0.00"));
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "CreditHc", Infoglove.Numbers.Decimals.ForceToDecimalString(rowValue, ',', "0.00"));

            //Description palkkalajilta
            string description = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "Description", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Description", description);

            string result = Infoglove.Sql.Transaction.Insert(connection, "PayrollRow", payrollRowValues);
            if (result == "0")
            {//Ei mennyt, päivitä
                string selectString = "SELECT PrimaryKey FROM PayrollRow WHERE RowNo=" + rowNo + " AND FKPayroll = " + fkPayroll;
                string fkPayrollRow = Infoglove.Sql.Transaction.GetString(connection, selectString);
                if (!string.IsNullOrEmpty(fkPayrollRow))
                {
                    Infoglove.Sql.Dictionary.Add(payrollRowValues, "PrimaryKey", fkPayrollRow);
                    result = Infoglove.Sql.Transaction.Update(connection, "PayrollRow", payrollRowValues);
                }
                else
                {//Ei päivittynyt
                    return "[ERROR] trying to insert automatic money wage row, please contact support";
                }
            }

            return result;

        }

        private string UpdateAutomationRow(SqlConnection connection, DataTable dtPayrollRow, string fkPayroll, string payrollType, decimal quantity, decimal calcValue)
        {//Lisää työeläkkeen, tapaturmavakuutuksen, tiliöintirivi, toimii vain Karhulan valimolla

            string fkPayrollType = Infoglove.Sql.Transaction.GetColumnByUniqId(connection, "PayrollType", "PrimaryKey", payrollType);

            System.Collections.Generic.Dictionary<string, string> payrollRowValues = new System.Collections.Generic.Dictionary<string, string>();

            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKPayroll", fkPayroll);
            int rowNo = Infoglove.Numbers.Integers.StringToInteger(payrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "RowNo", rowNo);

            //Palkkalaji
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKPayrollType", fkPayrollType);
            payrollType = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "PayrollType", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "PayrollType", payrollType);

            //Debet-tili, tunti- ja kuukausipalkkaisilla on eri tili
            string fkEmployee = this.GetFKEmployee(connection);// Infoglove.Pages.Maintain.GetComboSelectedValue(this.Payroll_Employee);
            string fkAccount = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKAccount", fkPayrollType);//Tuntipalkkaisten tilinumero
            string account = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Account", "UniqId", fkAccount);
            string isPeriodical = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "IsPeriodical", fkEmployee);
            if(isPeriodical == "1")
            {//Kuukausipalkkalainen, tilinumero on tuhatta suurempi kuin tuntipalkkaisella, joka on palkkalajin takana
                int account_ = Infoglove.Numbers.Integers.StringToInteger(account);
                account_ = account_ + 1000;
                account = account_.ToString();
            }
            fkAccount = Infoglove.Sql.Transaction.GetColumnByUniqId(connection, "Account", "PrimaryKey", account);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKAccount", fkAccount);

            account = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "Account", fkAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Account", account);

            //Kredit-tili on sama kaikilla
            string fkCreditAccount = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKCreditAccount", fkPayrollType);
            string creditAccount = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "Account", fkCreditAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKCreditAccount", fkCreditAccount);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "CreditAccount", creditAccount);

            if (Infoglove.Library.Method.IsCompanyLicence("karh01"))
            {//Kustannuspaikka riville
                string fkCostCenter = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Employee", "FKCostCenter", fkEmployee);
                string costCenter = Infoglove.Sql.Transaction.GetUniqIdAndNameByPrimaryKey(connection, "CostCenter", fkCostCenter);
                Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKCostCenter", fkCostCenter);
                Infoglove.Sql.Dictionary.Add(payrollRowValues, "CostCenter", costCenter);
            }

            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Quantity", quantity);

            //Yksikkö on luultavasti prosenttia
            string fkUnit = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "FKUnit", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "FKUnit", fkUnit);
            string unit = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "Unit", "UniqId", fkUnit);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Unit", unit);

            //Kertoimen avulla lasketaan prosentti
            decimal coefficient = 0.01m;
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Coefficient", coefficient);

            //Laske rivin arvo
            decimal rowValue = Infoglove.Business.Payroll.CalculateRowValue(connection, quantity, ref calcValue, coefficient, string.Empty, string.Empty, string.Empty);//Ei tarvitse tarkistaa maksimiarvoja!
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "CalcValue", calcValue);

            Infoglove.Sql.Dictionary.Add(payrollRowValues, "RowValue", rowValue);

            //Debet ja Kredit menevät saman suuruisina
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "DebetHc", rowValue);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "CreditHc", rowValue);

            //Description palkkalajilta
            string description = Infoglove.Sql.Transaction.GetColumnByPrimaryKey(connection, "PayrollType", "Description", fkPayrollType);
            Infoglove.Sql.Dictionary.Add(payrollRowValues, "Description", description);

            string result = Infoglove.Sql.Transaction.Insert(connection, "PayrollRow", payrollRowValues);
            if (result == "0")
            {//Ei mennyt, päivitä
                string selectString = "SELECT PrimaryKey FROM PayrollRow WHERE RowNo=" + rowNo + " AND FKPayroll = " + fkPayroll;
                string fkPayrollRow = Infoglove.Sql.Transaction.GetString(connection, selectString);
                if (!string.IsNullOrEmpty(fkPayrollRow))
                {
                    Infoglove.Sql.Dictionary.Add(payrollRowValues, "PrimaryKey", fkPayrollRow);
                    result = Infoglove.Sql.Transaction.Update(connection, "PayrollRow", payrollRowValues);
                }
                else
                {//Ei päivittynyt
                    return "[ERROR] trying to insert automatic money wage row, please contact support";
                }
            }

            return result;

        }

        private string UpdatePayrollStatisticsColumns(SqlConnection connection, string payroll)
        {//Päivitä tositeotsikon tilastokentät uudelleen tietokannassa olevien rivien perusteella

            string paymentDate = Infoglove.Dates.Convert.StringToDateId(this.Payroll_PaymentDate.Text);

            string selectString = "SELECT Payroll_UniqId," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_MoneyWage) AS Payroll_MoneyWage," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_FringeBenefit) AS Payroll_FringeBenefit," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_Allowance) AS Payroll_Allowance," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_TableWithholdIncome) AS Payroll_TableWithholdIncome," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_PercentWithholdIncome) AS Payroll_PercentWithholdIncome," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_TaxWithhold) AS Payroll_TaxWithhold," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_TradeUnionFee) AS Payroll_TradeUnionFee," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_EmployeePensionFee) AS Payroll_EmployeePensionFee," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_UnemplInsuranceFee) AS Payroll_UnemplInsuranceFee," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_OtherAllowance) AS Payroll_OtherAllowance," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_CostCompensation) AS Payroll_CostCompensation," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_PensionIncome) AS Payroll_PensionIncome," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_LelIncome) AS Payroll_LelIncome," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_TaelIncome) AS Payroll_TaelIncome," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_SotuIncome) AS Payroll_SotuIncome," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_CumulTylyValue) AS Payroll_CumulTylyValue," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_PaidTylyValue) AS Payroll_PaidTylyValue," + Infoglove.Library.Constant.crlfString +
            "SUM(Payroll_PaidTylyQuantity) AS Payroll_PaidTylyQuantity" + Infoglove.Library.Constant.crlfString +
            "FROM PayrollRowView WHERE Payroll_UniqId = '" + payroll + "' GROUP BY Payroll_UniqId";

            DataTable dtPayrollRow = new DataTable();
            string result = Infoglove.Sql.Transaction.FillDataTable(connection, dtPayrollRow, selectString);
            if (Infoglove.Library.Method.IsError(result))
                return result;
            System.Collections.Generic.Dictionary<string, string> payrollValues = new System.Collections.Generic.Dictionary<string, string>();
            if (dtPayrollRow.Rows.Count == 1)
            {//Tilasto löytyi 
                Infoglove.Sql.Dictionary.Add(payrollValues, "UniqId", payroll);

                decimal moneyWage = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_MoneyWage");
                Infoglove.Sql.Dictionary.Add(payrollValues, "MoneyWage", moneyWage);

                decimal fringeBenefit = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_FringeBenefit");
                Infoglove.Sql.Dictionary.Add(payrollValues, "FringeBenefit", fringeBenefit);

                decimal allowance = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_Allowance");
                Infoglove.Sql.Dictionary.Add(payrollValues, "Allowance", allowance);

                decimal tableWithholdIncome = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_TableWithholdIncome");
                Infoglove.Sql.Dictionary.Add(payrollValues, "TableWithholdIncome", tableWithholdIncome);

                decimal percentWithholdIncome = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_PercentWithholdIncome");
                Infoglove.Sql.Dictionary.Add(payrollValues, "PercentWithholdIncome", percentWithholdIncome);

                decimal taxWithhold = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_TaxWithhold");
                Infoglove.Sql.Dictionary.Add(payrollValues, "TaxWithhold", taxWithhold);

                decimal tradeUnionFee = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_TradeUnionFee");
                Infoglove.Sql.Dictionary.Add(payrollValues, "TradeUnionFee", tradeUnionFee);

                decimal employeePensionFee = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_EmployeePensionFee");
                Infoglove.Sql.Dictionary.Add(payrollValues, "EmployeePensionFee", employeePensionFee);

                decimal unemplInsuranceFee = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_UnemplInsuranceFee");
                Infoglove.Sql.Dictionary.Add(payrollValues, "UnemplInsuranceFee", unemplInsuranceFee);

                decimal otherAllowance = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_OtherAllowance");
                Infoglove.Sql.Dictionary.Add(payrollValues, "OtherAllowance", otherAllowance);

                decimal costCompensation = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_CostCompensation");
                Infoglove.Sql.Dictionary.Add(payrollValues, "CostCompensation", costCompensation);

                decimal pensionIncome = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_PensionIncome");
                Infoglove.Sql.Dictionary.Add(payrollValues, "PensionIncome", pensionIncome);

                decimal telIncome = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_TelIncome");
                Infoglove.Sql.Dictionary.Add(payrollValues, "TelIncome", telIncome);

                decimal lelIncome = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_LelIncome");
                Infoglove.Sql.Dictionary.Add(payrollValues, "LelIncome", lelIncome);

                decimal taelIncome = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_TaelIncome");
                Infoglove.Sql.Dictionary.Add(payrollValues, "TaelIncome", taelIncome);

                decimal sotuIncome = Infoglove.AdoNet.DataTable.DecimalValueNullIsZero(dtPayrollRow, 0, "Payroll_SotuIncome");
                Infoglove.Sql.Dictionary.Add(payrollValues, "SotuIncome", sotuIncome);

                //Päivitä myös työeläkemaksu otsikolla olevien tietojen perusteella
                decimal pensionFeePercent = Infoglove.Numbers.Decimals.StringToDecimal(this.Payroll_PensionFeePercent.Text);
                if (pensionFeePercent != 0)
                {//Prosentti on olemassa, laske
                    decimal pensionFee = pensionFeePercent * pensionIncome / 100m;
                    pensionFee = Infoglove.Numbers.Decimals.Round(pensionFee, 2);
                    Infoglove.Sql.Dictionary.Add(payrollValues, "PensionFee", pensionFee);
                }

                result = Infoglove.Sql.Transaction.Update(connection, "Payroll", payrollValues);
            }

            return result;

        }

       
        protected void PayrollRow_Grid_CustomButtonCallback(object sender, ASPxGridViewCustomButtonCallbackEventArgs e)
        {//Käsitellään erikoisnäppäimen eli tässä tapauksessa editointibuttonin painallus
            try
            {//Hae primääriavain
                string primaryKey = this.PayrollRow_Grid.GetRowValues(e.VisibleIndex, "PayrollRow_PrimaryKey").ToString();
                this.PayrollRow_PrimaryKey.Text = primaryKey;
                //Hae detailin primääriavaimen perusteella kontrollien sisältö
                this.master.GetDetailEntity(this.detailEntity, primaryKey);
                this.master.StoreInputControls(this.detailEntity);//säilötään ne
                this.master.SetMaintenanceMode(Infoglove.Library.Constant.modeMaintain);

            }
            catch (Exception e2)
            {
                this.master.ShowErrorMessage("[ERROR] editing grid row, please contact support, " + e2.Message);
                return;
            }
        }

        protected void SendColumnsToClient(object sender, CustomJSPropertiesEventArgs e)
        {//Siirrä tarvittavat masterin kentät browserille, näytetään JavaScript-metodissa ShowJSProperties

            e.Properties["cp_lblMaintenanceMessage"] = this.master.lblMaintenanceMessage.Text;
            e.Properties["cp_lblErrorMessage"] = this.master.lblErrorMessage.Text;
            if (!this.master.TestSession(this.CompanyId.Text)) 
                return;//Testaa, onko sessio hengissä ja ollaanko oikeassa yrityksessä
            e.Properties["cp_CompanyId"] = Infoglove.Session.Property.companyId.ToLower();//Näin on helpompi testata JavaScriptissa
            e.Properties["cp_CallbackParameter"] = this.CallbackParameter.Text;
            e.Properties["cp_MasterSearch"] = this.MasterSearch.Text;//Huom! Luetaan instanssimuuttujasta

            string rp_DocumentMap_Visible = "false";
            if (this.rp_DocumentMap.Visible)
                rp_DocumentMap_Visible = "true";
            e.Properties["cp_rp_DocumentMap_Visible"] = rp_DocumentMap_Visible;

            //Masterin kentät
            e.Properties["cp_Payroll_PrimaryKey"] = this.Payroll_PrimaryKey.Text;
            e.Properties["cp_Payroll_UniqId"] = this.Payroll_UniqId.Text;
            e.Properties["cp_Payroll_Employee"] = this.Payroll_Employee.Text;
            e.Properties["cp_Payroll_PayrollPeriod"] = this.Payroll_PayrollPeriod.Text;
            e.Properties["cp_Payroll_Language"] = this.Payroll_Language.Text;
            
            e.Properties["cp_Payroll_TaxDayCount"] = this.Payroll_TaxDayCount.Text;
            e.Properties["cp_Payroll_Description"] = this.Payroll_Description.Text;
            e.Properties["cp_Payroll_WithholdCalcDoc"] = this.Payroll_WithholdCalcDoc.Text;
            e.Properties["cp_Payroll_HolidayCalcDoc"] = this.Payroll_HolidayCalcDoc.Text;

            e.Properties["cp_Payroll_WorkHours"] = this.Payroll_WorkHours.Text;
            e.Properties["cp_Payroll_PeriodStartDate"] = this.Payroll_PeriodStartDate.Text;
            e.Properties["cp_Payroll_PeriodEndDate"] = this.Payroll_PeriodEndDate.Text;
            e.Properties["cp_Payroll_PaymentDate"] = this.Payroll_PaymentDate.Text;

            e.Properties["cp_Payroll_MoneyWage"] = this.Payroll_MoneyWage.Text;
            e.Properties["cp_Payroll_FringeBenefit"] = this.Payroll_FringeBenefit.Text;

            e.Properties["cp_Payroll_Allowance"] = this.Payroll_Allowance.Text;
            e.Properties["cp_Payroll_CostCompensation"] = this.Payroll_CostCompensation.Text;

            e.Properties["cp_Payroll_TaxDayCount"] = this.Payroll_TaxDayCount.Text;
            e.Properties["cp_Payroll_WorkHours"] = this.Payroll_WorkHours.Text;
            e.Properties["cp_Payroll_MoneyWage"] = this.Payroll_MoneyWage.Text;
            e.Properties["cp_Payroll_FringeBenefit"] = this.Payroll_FringeBenefit.Text;
            e.Properties["cp_Payroll_Allowance"] = this.Payroll_Allowance.Text;

            e.Properties["cp_Payroll_Allowance"] = this.Payroll_Allowance.Text;
            e.Properties["cp_Payroll_CostCompensation"] = this.Payroll_CostCompensation.Text;
            e.Properties["cp_Payroll_TableWithholdIncome"] = this.Payroll_TableWithholdIncome.Text;
            e.Properties["cp_Payroll_PercentWithholdIncome"] = this.Payroll_PercentWithholdIncome.Text;

            e.Properties["cp_Payroll_TaxWithhold"] = this.Payroll_TaxWithhold.Text;

            e.Properties["cp_Payroll_TradeUnionFee"] = this.Payroll_TradeUnionFee.Text;

            e.Properties["cp_Payroll_EmployeePensionFee"] = this.Payroll_EmployeePensionFee.Text;
            e.Properties["cp_Payroll_UnemplInsuranceFee"] = this.Payroll_UnemplInsuranceFee.Text;

            e.Properties["cp_Payroll_OtherAllowance"] = this.Payroll_OtherAllowance.Text;
            e.Properties["cp_Payroll_PaymentValue"] = this.Payroll_PaymentValue.Text;
            
            e.Properties["cp_Payroll_PensionIncome"] = this.Payroll_PensionIncome.Text;
            e.Properties["cp_Payroll_TelIncome"] = this.Payroll_TelIncome.Text;
            e.Properties["cp_Payroll_LelIncome"] = this.Payroll_LelIncome.Text;
            e.Properties["cp_Payroll_TaelIncome"] = this.Payroll_TaelIncome.Text;

            e.Properties["cp_Payroll_PensionFee"] = this.Payroll_PensionFee.Text;
            e.Properties["cp_Payroll_PensionFeePercent"] = this.Payroll_PensionFeePercent.Text;
            e.Properties["cp_Payroll_SocialSecurityPercent"] = this.Payroll_SocialSecurityPercent.Text;

            e.Properties["cp_Payroll_SotuIncome"] = this.Payroll_SotuIncome.Text;
            e.Properties["cp_Payroll_WorkDaysMonth1"] = this.Payroll_WorkDaysMonth1.Text;

            e.Properties["cp_Payroll_WorkDaysMonth2"] = this.Payroll_WorkDaysMonth2.Text;
            e.Properties["cp_Payroll_AbsenceDaysMonth1"] = this.Payroll_AbsenceDaysMonth1.Text;
            e.Properties["cp_Payroll_AbsenceDaysMonth2"] = this.Payroll_AbsenceDaysMonth2.Text;
            e.Properties["cp_Payroll_HolidayAccDaysMonth1"] = this.Payroll_HolidayAccDaysMonth1.Text;
            e.Properties["cp_Payroll_HolidayAccDaysMonth2"] = this.Payroll_HolidayAccDaysMonth2.Text;

            e.Properties["cp_Payroll_HolidayAccHoursMonth1"] = this.Payroll_HolidayAccHoursMonth1.Text;
            e.Properties["cp_Payroll_HolidayAccHoursMonth2"] = this.Payroll_HolidayAccHoursMonth2.Text;

            e.Properties["cp_Payroll_UnpaidHolidayDaysPrevYear"] = this.Payroll_UnpaidHolidayDaysPrevYear.Text;
            e.Properties["cp_Payroll_UnpaidHolidayDaysThisYear"] = this.Payroll_UnpaidHolidayDaysThisYear.Text;

            e.Properties["cp_Payroll_PayrollSendingType"] = this.Payroll_PayrollSendingType.Text;
            e.Properties["cp_Payroll_PayrollSendingDate"] = this.Payroll_PayrollSendingDate.Text;
            e.Properties["cp_Payroll_PayrollSendingTime"] = this.Payroll_PayrollSendingTime.Text;
           
            e.Properties["cp_Payroll_UnpaidTylyPrevYear"] = this.Payroll_UnpaidTylyPrevYear.Text;
            e.Properties["cp_Payroll_UnpaidTylyThisYear"] = this.Payroll_UnpaidTylyThisYear.Text;
            e.Properties["cp_Payroll_EarnedTylyValue"] = this.Payroll_EarnedTylyValue.Text;
            e.Properties["cp_Payroll_PaidTylyValue"] = this.Payroll_PaidTylyValue.Text;

            //Detailin kentät
            e.Properties["cp_PayrollRow_RowNo"] = this.PayrollRow_RowNo.Text;
            e.Properties["cp_PayrollRow_PayrollType"] = this.PayrollRow_PayrollType.Text;

            e.Properties["cp_PayrollRow_Account"] = this.PayrollRow_Account.Text;
            e.Properties["cp_PayrollRow_CreditAccount"] = this.PayrollRow_CreditAccount.Text;
            e.Properties["cp_PayrollRow_CostCenter"] = this.PayrollRow_CostCenter.Text;
            e.Properties["cp_PayrollRow_Project"] = this.PayrollRow_Project.Text;
            e.Properties["cp_PayrollRow_WorkOrder"] = this.PayrollRow_WorkOrder.Text;

            e.Properties["cp_PayrollRow_Quantity"] = this.PayrollRow_Quantity.Text;
            e.Properties["cp_PayrollRow_QuantityFormula"] = this.PayrollRow_QuantityFormula.Text;

            e.Properties["cp_PayrollRow_CalcValue"] = this.PayrollRow_CalcValue.Text;
            e.Properties["cp_PayrollRow_CalcValueFormula"] = this.PayrollRow_CalcValueFormula.Text;

            e.Properties["cp_PayrollRow_Unit"] = this.PayrollRow_Unit.Text;

            e.Properties["cp_PayrollRow_Coefficient"] = this.PayrollRow_Coefficient.Text;

            e.Properties["cp_PayrollRow_RowValue"] = this.PayrollRow_RowValue.Text;
            e.Properties["cp_PayrollRow_Description"] = this.PayrollRow_Description.Text;

        }

        protected void PayrollRow_Grid_Callback(object source, CallbackEventArgsBase e)
        {//Ei käytössä eikä toimi
            string[] parameters = e.Parameter.Split(':');
        }

    }
}

