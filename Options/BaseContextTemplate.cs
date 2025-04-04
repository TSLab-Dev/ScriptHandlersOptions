﻿using System;
using System.Collections.Generic;
using System.Linq;
using TSLab.DataSource;

namespace TSLab.Script.Handlers.Options
{
    /// <summary>
    /// \~englisg Base class for handlers with history (implements of BaseContextWithNumber)
    /// \~russian Базовый класс для блоков с историей (реализует BaseContextWithNumber)
    /// </summary>
    public abstract class BaseContextTemplate<T> : BaseContextHandler
        where T : struct
    {
        /// <summary>
        /// \~english Local temporary container
        /// \~russian Локальное кеширующее поле
        /// </summary>
        private T m_prevValue;

        /// <summary>
        /// Приватное кеширующее поле для истории (ключ -- время бара)
        /// </summary>
        private Dictionary<DateTime, T> m_privateCache;

        /// <summary>
        /// Проверка валидности вычисленного значения (например, волатильность должна быть числом больше 0)
        /// </summary>
        /// <param name="val">проверяемое значение</param>
        /// <returns>true, если всё в порядке</returns>
        protected virtual bool IsValid(T val)
        {
            return true;
        }

        /// <summary>
        /// Значение на случай невозможности выполнить расчет с учетом флага repeatLastValue
        /// </summary>
        /// <param name="repeatLastValue">использовать последнее известное значение повторно?</param>
        // ReSharper disable once VirtualMemberNeverOverriden.Global        
        protected virtual T GetFailRes(bool repeatLastValue)
        {
            T failRes;
            if (repeatLastValue)
                failRes = IsValid(m_prevValue) ? m_prevValue : default(T);
            else
                failRes = default(T);
            return failRes;
        }

        /// <summary>
        /// \~english Get history of previous values
        /// \~russian История предыдущих значений
        /// </summary>
        /// <param name="cashKey">ключ кеша</param>
        /// <param name="useGlobalCacheForHistory">искать серию исторических данных в Глобальном Кеше?</param>
        /// <param name="fromStorage">читать с диска</param>
        /// <returns>коллекция с историей</returns>
        // ReSharper disable once VirtualMemberNeverOverriden.Global
        protected virtual Dictionary<DateTime, T> GetHistory(string cashKey, bool useGlobalCacheForHistory, bool fromStorage = false)
        {
            if ((m_context == null) || String.IsNullOrWhiteSpace(cashKey))
                return null;

            if (m_privateCache == null)
            {
                Dictionary<DateTime, T> history = null;
                if (useGlobalCacheForHistory)
                {
                    var obj = m_context.LoadGlobalObject(cashKey, fromStorage);
                    switch (obj)
                    {
                        case Dictionary<DateTime, T> t:
                            history = t;
                            break;
                        case NotClearableContainer<Dictionary<DateTime, T>> t:
                            history = t.Content;
                            break;
                    }

                    if (history == null)
                    {
                        string msg = String.Format("[{0}] GLOBAL history '{1}' not found in global cache for key.GetHashCode: {2}",
                            m_context.Runtime.TradeName ?? "EMPTY", cashKey, cashKey.GetHashCode());
                        m_context.Log(msg, MessageType.Info, false);

                        history = new Dictionary<DateTime, T>();
                        var data = new NotClearableContainer<Dictionary<DateTime, T>>(history);
                        m_context.StoreGlobalObject(cashKey, data, fromStorage);
                    }
                }
                else
                {
                    var obj = m_context.LoadObject(cashKey, fromStorage);
                    switch (obj)
                    {
                        case Dictionary<DateTime, T> t:
                            history = t;
                            break;
                        case NotClearableContainer<Dictionary<DateTime, T>> t:
                            history = t.Content;
                            break;
                    }

                    if (history == null)
                    {
                        string msg = String.Format("[{0}] Local history '{1}' not found in local cache for key.GetHashCode: {2}",
                            m_context.Runtime.TradeName ?? "EMPTY", cashKey, cashKey.GetHashCode());
                        m_context.Log(msg, MessageType.Info, false);

                        history = new Dictionary<DateTime, T>();
                        var data = new NotClearableContainer<Dictionary<DateTime, T>>(history);
                        m_context.StoreObject(cashKey, data, fromStorage);
                    }
                }

                m_privateCache = history;
            }
            
            return m_privateCache;
        }

        protected virtual void SaveHistory(Dictionary<DateTime, T> history, string cashKey,
                                           bool useGlobalCacheForHistory, bool isStorage = false)
        {
            lock (history)
            {
                var data = new NotClearableContainer<Dictionary<DateTime, T>>(history);
                if (useGlobalCacheForHistory)
                    m_context.StoreGlobalObject(cashKey, data, isStorage);
                else
                    m_context.StoreObject(cashKey, data, isStorage);
                m_privateCache = null;
            }
        }

        /// <summary>
        /// Выполнить фактический расчет значения с использованием истории, разбором аргументов и индикацией успеха
        /// </summary>
        /// <param name="history">словарь с историей предыдущих значений</param>
        /// <param name="now">текущее время этой точки</param>
        /// <param name="barNum">индекс бара</param>
        /// <param name="args">произвольные аргументы</param>
        /// <param name="val">вычисленное значение</param>
        /// <returns>true, если расчет получился</returns>
        protected abstract bool TryCalculate(Dictionary<DateTime, T> history, DateTime now, int barNum, object[] args, out T val);

        /// <summary>
        /// Общие танцы вокруг локальных кешей (поиск в истории, повтор точки в случае проблем с поиском,
        /// вызов фактического алгоритма расчета для получения новых значений)
        /// </summary>
        /// <param name="cashKey">ключ кеша ИСТОРИИ</param>
        /// <param name="now">текущее время</param>
        /// <param name="repeatLastValue">повтор значения</param>
        /// <param name="printInMainLog">вывод сообщений в главный лог программы</param>
        /// <param name="useGlobalCacheForHistory">искать серию исторических данных в Глобальном Кеше?</param>
        /// <param name="barNum">индекс бара</param>
        /// <param name="args">произвольные аргументы</param>
        /// <param name="isStorage">читать/сохранять с диска</param>
        /// <param name="updateHistory">сохранять ли результат</param>
        /// <param name="maxValues">сколько значений сохранять (если 0, то maxValues = количество баров)</param>
        /// <returns>значение для точки с указанным индексом</returns>
        // ReSharper disable once VirtualMemberNeverOverriden.Global        
        protected virtual T CommonExecute(string cashKey, DateTime now, bool repeatLastValue, bool printInMainLog, 
                                          bool useGlobalCacheForHistory, int barNum, object[] args, 
                                          bool isStorage = false, bool updateHistory = false, int maxValues = 0)
        {
            // 1. Подготовка значения на случай проблем
            T failRes = GetFailRes(repeatLastValue);

            // 3. Проверка на случай совсем странного вызова
            int len = m_context.BarsCount;
            if (len <= 0)
                return failRes;

            // 5. Подготовка кеша
            Dictionary<DateTime, T> history = GetHistory(cashKey, useGlobalCacheForHistory, isStorage);
            if (history == null)
                return failRes;

            // 7. Погнали
            T val;
            //DateTime now = m_context.Runtime.GetBarTime(barNum); // sec.Bars[barNum].Date;
            //m_context.Log(String.Format("[DEBUG.CommonExecute] now: {0}", now.ToString(DateTimeFormatWithMs, CultureInfo.InvariantCulture)), MessageType.Info, true);
            if ((history.TryGetValue(now, out val)) && IsValid(val))
            {
                m_prevValue = val;
                return val;
            }
            else
            {
                // В случае подвижного окна имеет смысл восстанавливать нулевую точку
                // из истории. Иначе возникают странные визуальные казусы.
                if ((barNum == 0) && (history.Count > 0))
                {
                    #region Отдельно обрабатываю нулевой бар
                    T tmp = default(T);
                    DateTime foundKey = new DateTime(1, 1, 1);
                    lock (history)
                    {
                        foreach (var kvp in history.ToArray())
                        {
                            if (kvp.Key > now)
                                continue;

                            if (foundKey < kvp.Key)
                            {
                                foundKey = kvp.Key;
                                tmp = kvp.Value;
                            }
                        }
                    }

                    if ((foundKey.Year > 1) && IsValid(tmp))
                    {
                        val = tmp;
                        m_prevValue = val;
                        return val;
                    }
                    #endregion Отдельно обрабатываю нулевой бар
                }

                int barsCount = ContextBarsCount;
                if (barNum < barsCount - 1)
                {
                    // Если история содержит осмысленное значение, то оно уже содержится в failRes
                    return failRes;
                }
                else
                {
                    try
                    {
                        if (TryCalculate(history, now, barNum, args, out val) && IsValid(val))
                        {
                            m_prevValue = val;
                            // [2019-06-07] Как правило, кубик с вычислениями должен обновить историю. Но не всегда.
                            lock (history)
                            {
                                TryUpdateHistory(history, history, now, val);

                                if (updateHistory)
                                {
                                    if (maxValues <= 0)
                                        maxValues = barsCount;
                                    var keys = history.Keys.OrderByDescending(x => x).Skip(maxValues).ToList();
                                    keys.ForEach(x => history.Remove(x));

                                    SaveHistory(history, cashKey, useGlobalCacheForHistory, isStorage);
                                }
                            }

                            return val;
                        }
                    }
                    catch (Exception ex)
                    {
                        m_context.Log(ex.ToString(), MessageType.Error, printInMainLog);
                        //throw;
                    }

                    return failRes;
                }
            }
        }

        /// <summary>
        /// Метод обновляет коллекцию history, при необходимости делает синхронизацию.
        /// </summary>
        /// <param name="syncObj">объект-синхронизации</param>
        /// <param name="history">коллекция с данными (словарь)</param>
        /// <param name="dateTimeKey">точное время, к которому относится точка данных</param>
        /// <param name="newVal">новые данные для записи в историю</param>
        /// <returns>флаг успешности записи</returns>
        // ReSharper disable once VirtualMemberNeverOverriden.Global    
        protected virtual bool TryUpdateHistory(object syncObj,
            Dictionary<DateTime, T> history, DateTime dateTimeKey, T newVal)
        {
            if (syncObj == null)
            {
                history[dateTimeKey] = newVal;
            }
            else
            {
                // Блокировка на случай если кто-то сейчас итерируется по истории
                lock (syncObj)
                {
                    history[dateTimeKey] = newVal;
                }
            }

            return true;
        }
    }
}
