using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class BMP402 : Robot
    {
        #region Version
        // BMP400 - стартовая версия торговли автокорреляции
        // BMP401 - будет работать внутри дня
        // BMP402 - настройка торговли по барам автокорреляции
        // из расчетов убраны выходные дни
        // вариант с "торговлей" относительно машки
        // добавлено
        #endregion

        #region Parameters
        [Parameter("BMPE300", Group = "Strategy", DefaultValue = "BMPE300")]
        public string LabelNameStrategy { get; set; } // название стратегии
        public enum enmDebaggerTrue { yes, no }
        [Parameter("DebaggerTrue", Group = "Strategy", DefaultValue = enmDebaggerTrue.yes)]
        public enmDebaggerTrue DebaggerTrue { get; set; } // отключение дебаггинга
        public enum enmDrawTrue { yes, no }
        [Parameter("DrawTrue", Group = "Strategy", DefaultValue = enmDrawTrue.no)]
        public enmDrawTrue DrawTrue { get; set; } // отключение рисования
        [Parameter("SpreadMaxForTrade", Group = "Strategy", DefaultValue = 25)]
        public int SpreadMaxForTrade { get; set; }  // максимально допустимый размер спреда для торговли
        [Parameter("StartSize", Group = "MM parameters", DefaultValue = 1)]
        public int StartSize { get; set; } // размер начального входа в позицию
        [Parameter("StartBarsAutocor", Group = "Autocor parameters", DefaultValue = 1)]
        public int StartBarsAutocor { get; set; } // 
        [Parameter("StopBarsAutocor", Group = "Autocor parameters", DefaultValue = 10)]
        public int StopBarsAutocor { get; set; } //
        [Parameter("StepBarsAutocor", Group = "Autocor parameters", DefaultValue = 1)]
        public int StepBarsAutocor { get; set; } //
        [Parameter("AverageRezult", Group = "Autocor parameters", DefaultValue = 1)]
        public int AverageRezult { get; set; } // по скольки значениям результатов считаем среднюю
        public enum activRevers { activ, revers, activRevers }
        [Parameter("ActivRevers", Group = "Autocor parameters", DefaultValue = activRevers.activRevers)]
        public activRevers ActivRevers { get; set; } // по зпдумке как торговать: прямая, реверсивная автокорреляция
        [Parameter("TradeBarAutoCor", Group = "Strategy", DefaultValue = 13)]
        public int TradeBarAutoCor { get; set; } // Какой бар автокорреляции торгуем
        [Parameter("SMAperiod", Group = "Strategy", DefaultValue = 14)]
        public int SMAperiod { get; set; } // размер машки, относительно которой меняется прямая или инверсная торговля
        [Parameter("RegressionPeriod", Group = "Regression", DefaultValue = 20)]
        public int RegressionPeriod { get; set; } // 
        [Parameter("DegresBar", Group = "Regression", DefaultValue = 2)]
        public int DegresBar { get; set; } // за сколько баров (значений регрессии) будет считаться угол
        [Parameter("DegresUp", Group = "Regression", DefaultValue = 33)]
        public int DegresLong { get; set; } // меньше этого значения угла на регресии входим в лонг
        [Parameter("DegresShort", Group = "Regression", DefaultValue = -60)]
        public int DegresShort { get; set; } // меньше этого значения угла на регресии входим в шорт
        #endregion

        #region Peremen
        private readonly LongShortParametrs PS = new();
        public IEnumerable<Position> PositionLong;
        public IEnumerable<Position> PositionShort;
        public IEnumerable<Position> PositionTraling;
        public IEnumerable<Position> PositionAverageLoss;
        public List<double> listRezult = new List<double>();
        public List<List<double>> allListLine = new List<List<double>>();
        public List<double> dayRange = new List<double>();
        public List<double> listTrade = new List<double>();
        public List<List<double>> allListTrade = new List<List<double>>();
        public List<double> regresPiontList = new List<double>();
        double degrees = 0.0;

        #endregion

        protected override void OnStart()
        {
            if (DebaggerTrue == enmDebaggerTrue.yes)
            {
                var result = System.Diagnostics.Debugger.Launch();
            }
            PositionLong = Positions.Where(x => x.Label == LabelNameStrategy && x.SymbolName == Symbol.Name && x.TradeType == TradeType.Buy);
            PositionShort = Positions.Where(x => x.Label == LabelNameStrategy && x.SymbolName == Symbol.Name && x.TradeType == TradeType.Sell);
            for (var i = StartBarsAutocor; i <= StopBarsAutocor + 1; i++)
            {
                listRezult = new List<double>();
                allListLine.Add(listRezult);

            }
            for (var i = StartBarsAutocor; i <= StopBarsAutocor + 1; i++)
            {
                listTrade = new List<double>();
                allListTrade.Add(listTrade);
            }
            PS.OpenDayOld = Bars.OpenPrices.Last(0);
            PS.TrigTrade = false;
        }

        protected override void OnTick()
        {
            if (Bars.Count != PS.OldBar)
            {
                PS.OldBar = Bars.Count;

                CalcDayInfo(); //

            }
        }

        /// <summary>
        /// сбор информации по вирт эквити
        /// </summary>
        protected void VirtEquity()
        {
            var lastDay = dayRange.Count - 1;
            var profitLoss = Math.Round(PS.DayRange / Symbol.TickSize * Symbol.TickValue * 0.01, 2);
            var perem = 0.0;
            var sumRes = 0.0;
            for (var i = StartBarsAutocor; i <= StopBarsAutocor; i++) // прямая автокора
            {
                if (listTrade.Count > 0) { 
                    sumRes = allListTrade[i][listTrade.Count - 1]; 
                }
                if (listRezult.Count > 0) { 
                    perem = allListLine[i][listRezult.Count - 1]; 
                }
                #region запись результатов "торговли" по углу регрессии
                //if (PS.DayRange > 0 && dayRange[lastDay - i] > 0 || PS.DayRange < 0 && dayRange[lastDay - i] < 0)
                //{
                //    if (listRezult.Count - 1 > RegressionPeriod)
                //    {
                //        if (CalculateDegres(i) && PS.DegresUp) // углы в допустимых диапазонах
                //        {
                //            allListTrade[i].Add(sumRes + Math.Abs(profitLoss));
                //        }
                //    }
                //}
                #endregion
                if (PS.DayRange < 0 && dayRange[lastDay - i] < 0 ||PS.DayRange > 0 && dayRange[lastDay - i] > 0)
                {
                    #region запись результатов "торговли" по средней
                    if (listRezult.Count - 1 > SMAperiod)
                    {
                        if (CalculateAverage(i, allListLine[i][listRezult.Count - 1]))
                        {
                            allListTrade[i].Add(sumRes + Math.Abs(profitLoss));
                        }
                        else { allListTrade[i].Add(sumRes - Math.Abs(profitLoss)); }
                    }
                    #endregion
                    #region запись результатов "торговли" по углу регрессии
                    //if (listRezult.Count - 1 > RegressionPeriod)
                    //{
                    //    if (CalculateDegres(i))
                    //    {
                    //        allListTrade[i].Add(sumRes + Math.Abs(profitLoss));
                    //    }
                    //    else { allListTrade[i].Add(sumRes - Math.Abs(profitLoss)); }
                    //}
                    
                    #endregion
                    // allListLine[i].Add(Math.Abs(profitLoss)); // вариант при котором в лист писались значения
                    allListLine[i].Add(perem + Math.Abs(profitLoss)); // при этом варианте в лист пишется накoпленная эквити
                }                
                else
                {
                    #region запись результатов "торговли" по средней
                    if (listRezult.Count - 1 > SMAperiod)
                    {
                        if (!CalculateAverage(i, allListLine[i][listRezult.Count - 1]))
                        {
                            allListTrade[i].Add(sumRes + Math.Abs(profitLoss));
                        }
                        else { allListTrade[i].Add(sumRes - Math.Abs(profitLoss)); }
                    }
                    #endregion
                    #region запись результатов "торговли" по углу регрессии
                    //if (listRezult.Count - 1 > RegressionPeriod)
                    //{
                    //    if (!CalculateDegres(i))
                    //    {

                    //    }
                    //}
                    #endregion
                    // allListLine[i].Add( - Math.Abs(profitLoss)); 
                    allListLine[i].Add(perem - Math.Abs(profitLoss));
                }
            }
            #region на подумать по инверсным
            //for (var i = - StartBarsAutocor; i >= - StopBarsAutocor; i--) // инверсная автокора
            //{
            //    if (PS.DayRange < 0 && dayRange[lastDay - i] > 0)
            //    {
            //        allListLine[i].Add(Math.Abs(dayRange[lastDay]));
            //    }
            //    else if (PS.DayRange > 0 && dayRange[lastDay - i] < 0)
            //    {
            //        allListLine[i].Add(Math.Abs(dayRange[lastDay]));
            //    }
            //    else
            //    {
            //        allListLine[i].Add( - Math.Abs(dayRange[lastDay]));
            //    }
            //}
            #endregion
            DrawingInfo();
        }

        /// <summary>
        /// расчет угла
        /// </summary>
        protected bool CalculateDegres(int barsAnalize)
        {
            bool resilt = false;
            
            double sumX = 0, sumY = 0, sumXY = 0, sumXPower = 0;
            for (int i = 1; i < RegressionPeriod; i++)
            {
                sumXPower += i * i;
                sumX += i;
                sumY += allListLine[barsAnalize][listRezult.Count - i];
                sumXY += i * allListLine[barsAnalize][listRezult.Count - i];
            }
            var gh = (sumXPower * sumY - sumX * sumXY) / (sumXPower * RegressionPeriod - sumX * sumX); // рабочий
            if (regresPiontList.Count == 0) { regresPiontList.Add(gh); } //добавление первого знаяения
            else
            {
                if (regresPiontList.Count > 3)
                {
                    //for (int i = regresPiontList.Count - 1; i< regresPiontList.Count - 1 - )
                    var rasst = regresPiontList[regresPiontList.Count - 1] - regresPiontList[regresPiontList.Count - 1 - 2];
                    degrees = ((Math.Atan2(2, rasst) + 2 * Math.PI) * 180 / Math.PI) % 360;
                    degrees = degrees > 90 ? 90 - degrees : degrees;
                    if(degrees < DegresLong)
                    {
                        PS.DegresUp = true;
                        resilt = true;
                    }
                    else if (degrees < DegresShort)
                    {
                        PS.DegresShort = true;
                        resilt = true;
                    }
                    //resilt = degrees < DegresUp ? PS.DegresUp == true : degrees < DegresShort ? PS.DegresShort == true : false;
                }
                if (regresPiontList[regresPiontList.Count - 1] != gh) { regresPiontList.Add(gh); } // добавление значения, отличного от последнего в списке
            }
            return resilt;
        }

        /// <summary>
        /// расчет средней
        /// </summary>
        protected bool CalculateAverage(int numberList, double virtRes)
        {
            bool res = false;
            var sum = 0.0;
            var step = 0;
            if(listRezult.Count - 1 > SMAperiod)
            {
                for (var i = listRezult.Count - 1 - SMAperiod; i < listRezult.Count - 1; i++)
                {
                    sum += allListLine[numberList][i];
                    step += 1;
                }
                res = virtRes > sum / step;
            }
            else { res = false; }
            return res;
        }

        /// <summary>
        /// рисовашка
        /// </summary>
        protected void DrawingInfo()
        {
            var textLine = "";
            var total = 0.0;
            for (var i = StartBarsAutocor; i <= StopBarsAutocor; i++) // пробежать по всем
            {
                string trade = listRezult.Count - 1 <= SMAperiod ? "no trade" : 
                    CalculateAverage(i, allListLine[i][listRezult.Count - 1]) ?
                    "direct" : "inverse";
                total += Math.Round(allListTrade[i][listTrade.Count - 1], 2);
                //textLine = textLine + i + " " + Math.Round(allListLine[i].Sum(), 2).ToString() + " / " + // переделал под накопление )
                textLine = textLine + i + " " + Math.Round(allListLine[i][listRezult.Count - 1], 2).ToString() + " / " +
                        trade.ToString() + " / " + Math.Round(degrees, 0).ToString() + " / " +
                        Math.Round(allListTrade[i][listTrade.Count - 1], 2).ToString() +
                        "\n\r";
                
             }
            textLine = textLine + "Total = " + Math.Round(total, 2).ToString();
            Chart.DrawStaticText("1", textLine, VerticalAlignment.Top, HorizontalAlignment.Left, Color.Violet);
            Chart.DrawStaticText("12", Math.Round(total, 2).ToString(), VerticalAlignment.Top, HorizontalAlignment.Right, Color.Orange);
        }

        /// <summary>
        /// торговля определенного бара автокорреляции
        /// </summary>
        protected void TradeAutoCor()
        {
            //закрыть все позиции
            if (PositionLong.Any())
            {
                foreach (var pos in PositionLong)
                {
                    ClosePosition(pos);
                }
            }
            if (PositionShort.Any())
            {
                foreach (var pos in PositionShort)
                {
                    ClosePosition(pos);
                }
            }

            if (dayRange.Count > StopBarsAutocor + 1)
            { // сбор данных по вирт позам начинается на след день (когда закроется поза открытая в этот день)
                VirtEquity();
            } 

            var analisDay = dayRange.Count - Math.Abs(TradeBarAutoCor); // какой бар использовать для анализа
            if (dayRange[analisDay] > 0)
            {
                // вход по прямой автокорреляции
                ExecuteMarketOrder(TradeBarAutoCor > 0 ? TradeType.Buy : TradeType.Sell,
                    Symbol.Name,
                    StartSize * Symbol.VolumeInUnitsStep,
                    LabelNameStrategy);
            }
            if (dayRange[analisDay] < 0)
            {
                // вход по прямой автокорреляции
                ExecuteMarketOrder(TradeBarAutoCor > 0 ? TradeType.Sell : TradeType.Buy,
                    Symbol.Name,
                    StartSize * Symbol.VolumeInUnitsStep,
                    LabelNameStrategy);
            }
        }

        /// <summary>
        /// Сохранение в лист инфы по дневкам
        /// </summary>
        protected void CalcDayInfo()
        {
            if (Bars.OpenTimes.Last(0).Day != Bars.OpenTimes.Last(1).Day) // новый бар нового дня
            {
                var t = Bars.OpenTimes.Last().DayOfWeek;
                var t1 = Symbol.MarketHours.TimeTillClose().ToString();
                PS.DayRange = Math.Round((Bars.ClosePrices.Last(1) - PS.OpenDayOld) / Symbol.TickSize, 0);
                // в понедельник запомнить цену открытия и разрешение на торговлю
                if (Bars.OpenTimes.Last().DayOfWeek == DayOfWeek.Monday)
                {
                    // Для часовиков подходит (2) ,для другого тф - надо подбирать
                    PS.DayRange = Math.Round((PS.CloseFraday - PS.OpenDayOld) / Symbol.TickSize, 0); // для вирт эквити берем пятницу
                    PS.OpenDayOld = Bars.OpenPrices.Last(0); // обновлено значение начала дня
                    if (dayRange.Count > StopBarsAutocor) // когда набралось достаточно дней
                    {
                        TradeAutoCor();
                    }
                }
                // со вторника по пятницу - торговля
                if (Bars.OpenTimes.Last().DayOfWeek == DayOfWeek.Tuesday ||
                        Bars.OpenTimes.Last().DayOfWeek == DayOfWeek.Wednesday ||
                            Bars.OpenTimes.Last().DayOfWeek == DayOfWeek.Thursday ||
                                Bars.OpenTimes.Last().DayOfWeek == DayOfWeek.Friday)
                {
                    dayRange.Add(PS.DayRange); // добавлен размер последнего (предыдущего) дня со знаком
                    PS.OpenDayOld = Bars.OpenPrices.Last(0); // обновлено значение начала дня
                    if (dayRange.Count > StopBarsAutocor) // когда набралось достаточно дней
                    {
                        TradeAutoCor();
                    }
                }
                //  в субботу добавить размер бара пятницы
                if (Bars.OpenTimes.Last().DayOfWeek == DayOfWeek.Sunday && dayRange.Count != 0)
                {
                    dayRange.Add(PS.DayRange); // добавлен размер последнего (предыдущего) дня со знаком
                    // для реалтайма закрытие сделаок в пятницу вечером закрыть все позиции
                    PS.CloseFraday = Bars.OpenPrices.Last(1);
                    if (PositionLong.Any())
                    {
                        foreach (var pos in PositionLong)
                        {
                            ClosePosition(pos);
                        }
                    }
                    if (PositionShort.Any())
                    {
                        foreach (var pos in PositionShort)
                        {
                            ClosePosition(pos);
                        }
                    }

                }

                // для реалтайма закрытие сделок в пятницу вечером
                //if(Bars.OpenTimes.Last().DayOfWeek == DayOfWeek.Friday && Bars.OpenTimes.Last(). == Symbol.MarketHours.TimeTillClose())
                {

                }
            }
        }
        class LongShortParametrs
        {
            public double OpenDayOld;

            public double DayRange;
            public double CloseFraday;
            public int OldBar;
            public bool TrigTrade;
            public bool DegresUp;
            public bool DegresShort;
            public double degres;
            public double FalseVirtRezult2;
            public int NumberTrueAutocor;
            public int NumberFalseAutocor;
        }
    }
}