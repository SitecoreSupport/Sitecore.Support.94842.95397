using Autofac;
using Autofac.Builder;
using Autofac.Core;
using AutofacContrib.CommonServiceLocator;
using AutofacContrib.SolrNet;
using AutofacContrib.SolrNet.Config;
using Microsoft.Practices.ServiceLocation;
using Sitecore.Configuration;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.SolrProvider;
using Sitecore.ContentSearch.SolrProvider.DocumentSerializers;
using SolrNet;
using SolrNet.Impl;
using SolrNet.Schema;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace Sitecore.Support.ContentSearch.SolrProvider.AutoFacIntegration
{
  public class AutoFacSolrStartUp : ISolrStartUp, IProviderStartUp
  {
    private readonly ContainerBuilder builder;
    private readonly SolrServers Cores;
    private IContainer container;

    public AutoFacSolrStartUp(ContainerBuilder builder)
    {
      if (!SolrContentSearchManager.IsEnabled)
        return;
      this.builder = builder;
      this.Cores = new SolrServers();
    }

    private ISolrCoreAdmin BuildCoreAdmin()
    {
      SolrConnection solrConnection = new SolrConnection(SolrContentSearchManager.ServiceAddress);
      if (SolrContentSearchManager.EnableHttpCache)
        solrConnection.Cache = this.container.Resolve<ISolrCache>() ?? (ISolrCache)new NullCache();
      return (ISolrCoreAdmin)new SolrCoreAdmin((ISolrConnection)solrConnection, this.container.Resolve<ISolrHeaderResponseParser>(), this.container.Resolve<ISolrStatusResponseParser>());
    }

    public void Initialize()
    {
      if (!SolrContentSearchManager.IsEnabled)
        throw new InvalidOperationException("Solr configuration is not enabled. Please check your settings and include files.");
      foreach (string core in SolrContentSearchManager.Cores)
        this.AddCore(core, typeof(Dictionary<string, object>), SolrContentSearchManager.ServiceAddress + "/" + core);
      this.builder.RegisterModule((IModule)new SolrNetModule(this.Cores));
      this.builder.RegisterType<SolrFieldBoostingDictionarySerializer>().As<ISolrDocumentSerializer<Dictionary<string, object>>>();
      this.builder.RegisterType<Sitecore.ContentSearch.SolrProvider.Parsers.SolrSchemaParser>().As<ISolrSchemaParser>();
      this.builder.RegisterType<HttpRuntimeCache>().As<ISolrCache>();
      foreach (SolrServerElement solrServerElement in this.Cores)
      {
        string serviceName = solrServerElement.Id + typeof(SolrConnection);
        NamedParameter[] parameters = new NamedParameter[]
        {
                        new NamedParameter("serverURL", solrServerElement.Url)
        };
        this.builder.RegisterType(typeof(SolrConnection)).Named(serviceName, typeof(ISolrConnection)).WithParameters(parameters).OnActivated(delegate (IActivatedEventArgs<object> args)
        {
          if (SolrContentSearchManager.EnableHttpCache)
          {
            ((SolrConnection)args.Instance).Cache = args.Context.Resolve<ISolrCache>();
          }
          int intSetting = Settings.GetIntSetting("Support.ContentSearch.Solr.ConnectionTimeout", 30000);
          ((SolrConnection)args.Instance).Timeout = intSetting;
        });
      }
      this.container = this.builder.Build(ContainerBuildOptions.None);
      ServiceLocator.SetLocatorProvider((ServiceLocatorProvider)(() => (IServiceLocator)new AutofacServiceLocator((IComponentContext)this.container)));
      SolrContentSearchManager.SolrAdmin = this.BuildCoreAdmin();
      SolrContentSearchManager.Initialize();
    }

    public void AddCore(string coreId, Type documentType, string coreUrl)
    {
      SolrServers cores = this.Cores;
      SolrServerElement configurationElement = new SolrServerElement();
      configurationElement.Id = coreId;
      string assemblyQualifiedName = documentType.AssemblyQualifiedName;
      configurationElement.DocumentType = assemblyQualifiedName;
      string str = coreUrl;
      configurationElement.Url = str;
      cores.Add(configurationElement);
    }

    public bool IsSetupValid()
    {
      if (!SolrContentSearchManager.IsEnabled)
        return false;
      ISolrCoreAdmin admin = this.BuildCoreAdmin();
      return SolrContentSearchManager.Cores.Select<string, CoreResult>((Func<string, CoreResult>)(defaultIndex => admin.Status(defaultIndex).First<CoreResult>())).All<CoreResult>((Func<CoreResult, bool>)(status => status.Name != null));
    }
  }
}
