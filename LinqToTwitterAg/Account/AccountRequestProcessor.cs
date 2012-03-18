﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using LinqToTwitter.Common;

using LitJson;

namespace LinqToTwitter
{
    /// <summary>
    /// handles query processing for accounts
    /// </summary>
    public class AccountRequestProcessor<T>
        : IRequestProcessor<T>
        , IRequestProcessorWithAction<T>
        where T : class
    {
        /// <summary>
        /// base url for request
        /// </summary>
        public virtual string BaseUrl { get; set; }

        /// <summary>
        /// Type of account query (VerifyCredentials or RateLimitStatus)
        /// </summary>
        internal AccountType Type { get; set; }

        /// <summary>
        /// extracts parameters from lambda
        /// </summary>
        /// <param name="lambdaExpression">lambda expression with where clause</param>
        /// <returns>dictionary of parameter name/value pairs</returns>
        public virtual Dictionary<string, string> GetParameters(System.Linq.Expressions.LambdaExpression lambdaExpression)
        {
            return
               new ParameterFinder<Account>(
                   lambdaExpression.Body,
                   new List<string> { 
                       "Type"
                   })
                   .Parameters;
        }

        /// <summary>
        /// builds url based on input parameters
        /// </summary>
        /// <param name="parameters">criteria for url segments and parameters</param>
        /// <returns>URL conforming to Twitter API</returns>
        public virtual Request BuildURL(Dictionary<string, string> parameters)
        {
            string url;

            const string typeParam = "Type";
            if (parameters == null || !parameters.ContainsKey("Type"))
                throw new ArgumentException("You must set Type.", typeParam);

            Type = RequestProcessorHelper.ParseQueryEnumType<AccountType>(parameters[typeParam]);

            switch (Type)
            {
                case AccountType.VerifyCredentials:
                    url = BaseUrl + "account/verify_credentials.json";
                    break;
                case AccountType.RateLimitStatus:
                    url = BaseUrl + "account/rate_limit_status.json";
                    break;
                case AccountType.Totals:
                    url = BaseUrl + "account/totals.json";
                    break;
                case AccountType.Settings:
                    url = BaseUrl + "account/settings.json";
                    break;
                default:
                    throw new InvalidOperationException("The default case of BuildUrl should never execute because a Type must be specified.");
            }

            return new Request(url);
        }

        /// <summary>
        /// transforms json into IQueryable of Account
        /// </summary>
        /// <param name="responseJson">json with Twitter response</param>
        /// <returns>List of Account</returns>
        public virtual List<T> ProcessResults(string responseJson)
        {
            Account acct = null;

            if (!string.IsNullOrEmpty(responseJson))
            {
                switch (Type)
                {
                    case AccountType.Settings:
                        acct = HandleSettingsResponse(responseJson);
                        break;

                    case AccountType.VerifyCredentials:
                        acct = HandleVerifyCredentialsResponse(responseJson);
                        break;

                    case AccountType.RateLimitStatus:
                        acct = HandleRateLimitResponse(responseJson);
                        break;

                    case AccountType.Totals:
                        acct = HandleTotalsResponse(responseJson);
                        break;

                    default:
                        throw new InvalidOperationException("The default case of ProcessResults should never execute because a Type must be specified.");
                }
            }

            return new List<Account> { acct }.OfType<T>().ToList();
        }

        /// <summary>
        /// transforms json into an action response
        /// </summary>
        /// <param name="responseJson">json with Twitter response</param>
        /// <param name="theAction">Used to specify side-effect methods</param>
        /// <returns>Action response</returns>
        public virtual T ProcessActionResult(string responseJson, Enum theAction)
        {
            Account acct = null;

            if (!string.IsNullOrEmpty(responseJson))
            {
                switch ((AccountAction)theAction)
                {
                    case AccountAction.EndSession:
                        acct = HandleEndSessionResponse(responseJson);
                        break;

                    default:
                        throw new InvalidOperationException("The default case of ProcessActionResult should never execute because a Type must be specified.");
                }
            }

            return acct.ItemCast(default(T));
        }

        internal Account HandleSettingsResponse(string responseJson)
        {
            var settings = JsonMapper.ToObject(responseJson);
            var trendLocation = settings.GetValue<JsonData>("trend_location")[0];
            var sleepTime = settings.GetValue<JsonData>("sleep_time");
            var timeZone = settings.GetValue<JsonData>("time_zone");

            var acct = new Account
            {
                Type = Type,
                Settings = new Settings
                {
                    TrendLocation = Location.Create(trendLocation),
                    GeoEnabled = settings.GetValue<bool>("geo_enabled"),
                    SleepTime = 
                        new SleepTime
                        {
                            StartHour = sleepTime.GetValue<int>("start_time"),
                            EndHour = sleepTime.GetValue<int>("end_time"),
                            Enabled = sleepTime.GetValue<bool>("enabled")
                        },
                    Language = settings.GetValue<string>("language"),
                    AlwaysUseHttps = settings.GetValue<bool>("always_use_https"),
                    DiscoverableByEmail = settings.GetValue<bool>("discoverable_by_email"),
                    TimeZone = 
                        new TZInfo
                        {
                            Name = timeZone.GetValue<string>("name"),
                            TzInfoName = timeZone.GetValue<string>("tzinfo_name"),
                            UtcOffset = timeZone.GetValue<int>("utc_offset")
                        }
                }
            };


            return acct;
        }

        internal Account HandleRateLimitResponse(string responseJson)
        {
            var status = JsonMapper.ToObject(responseJson);

            var acct = new Account
            {
                Type = Type,
                RateLimitStatus = new RateLimitStatus
                {
                    HourlyLimit = status.GetValue<int>("hourly_limit"),
                    RemainingHits = status.GetValue<int>("remaining_hits"),
                    ResetTime = status.GetValue<string>("reset_time").GetDate(DateTime.MaxValue),
                    ResetTimeInSeconds = status.GetValue<int>("reset_time_in_seconds")
                }
            };


            return acct;
        }

        private Account HandleVerifyCredentialsResponse(string responseJson)
        {
            //var user = JsonMapper.ToObject(responseJson);

            //var acct = new Account
            //{
            //    Type = Type,
            //    User = User.Create(user)
            //};

            var serializer = Json.AccountConverter.GetSerializer();
            var user = serializer.Deserialize<Json.User>(responseJson);

            var acct = new Account
            {
                Type = Type,
                User = user.ToUser()
            };

            return acct;
        }

        private Account HandleTotalsResponse(string responseJson)
        {
            var serializer = Json.AccountConverter.GetSerializer();
            var totals = serializer.Deserialize<Json.Totals>(responseJson);

            var acct = new Account
            {
                Type = Type,
                Totals = new Totals
                {
                    Favorites = totals.favorites,
                    Followers = totals.followers,
                    Friends = totals.friends,
                    Updates = totals.updates
                }
            };

            return acct;
        }

        private Account HandleEndSessionResponse(string responseJson)
        {
            var serializer = Json.AccountConverter.GetSerializer();
            var endSession = serializer.Deserialize<Json.EndSession>(responseJson);

            var acct = new Account
            {
                EndSessionStatus = new TwitterHashResponse
                {
                    Request = endSession.request,
                    Error = endSession.error
                }
            };

            return acct;
        }
#if THESEDONTSEEMTOBEINJSON

            else if (twitterResponse.Name == "hash")
            {
                if (twitterResponse.Element("hourly-limit") != null)
                {
                    var rateLimits = new RateLimitStatus
                    {
                        HourlyLimit = int.Parse(twitterResponse.Element("hourly-limit").Value),
                        RemainingHits = int.Parse(twitterResponse.Element("remaining-hits").Value),
                        ResetTime = DateTime.Parse(twitterResponse.Element("reset-time").Value,
                                                    CultureInfo.InvariantCulture,
                                                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                        ResetTimeInSeconds = int.Parse(twitterResponse.Element("reset-time-in-seconds").Value)
                    };

                    acct.RateLimitStatus = rateLimits;
                }
                else if (twitterResponse.Element("request") != null)
                {
                    var endSession = new TwitterHashResponse
                    {
                        Request = twitterResponse.Element("request").Value,
                        Error = twitterResponse.Element("error").Value
                    };

                    acct.EndSessionStatus = endSession;
                }
            }
#endif
    }
}
