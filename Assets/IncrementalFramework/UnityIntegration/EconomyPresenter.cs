using System;
using System.Collections.Generic;
using IncrementalFramework.Core;
using IncrementalFramework.UI;
using IncrementalFramework.Unity.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IncrementalFramework.Unity
{
    /// <summary>The economy adapter projects its journal into observable presentation Values.</summary>
    public sealed class EconomyPresenter:IDisposable
    {
        private readonly EntityInstance state;
        private readonly Dictionary<string,Value<string>> texts=new Dictionary<string,Value<string>>();
        private readonly Dictionary<string,Value<bool>> enabled=new Dictionary<string,Value<bool>>();
        private readonly MountedUi view;
        private readonly Color accent;
        public EconomyPresenter(Transform parent,GameContentAsset content,Action click,
            Action<string> buyProducer,Action<string> buyUpgrade,Action doPrestige,Action save,Action load,Action<string> triggerRule,SampleTutorialSession tutorial=null)
        {
            accent=content.accentColor;
            var world=new EntityContext();state=new EntityInstance("economy-ui","presentation");world.Add(state);
            var definitions=new List<UiDefinition>();
            definitions.Add(Label("title",content.title,30,52));
            if(tutorial!=null)
            {
                var help=Ui.Text(TextTemplate.Read(_=>tutorial.Caption,.1));help.Layout.MinHeight=72;help.Style.FontSize=17;definitions.Add(help);
                var skip=Ui.Button("Skip tutorial",UiAction.Callback(tutorial.Skip));skip.Visible=UiBinding<bool>.From(_=>!tutorial.Completed,.1);skip.Layout.MinHeight=40;definitions.Add(skip);
            }
            definitions.Add(Label("introduction",content.introduction,17,88));
            definitions.Add(Label("offline","Offline progress cap: "+content.maximumOfflineSeconds.ToString("F0")+" seconds. Progress saves automatically every 30 seconds.",17,48));
            foreach(var resource in content.resources)
            {
                string id=resource.id,key="resource:"+id;
                definitions.Add(resource==content.clickResource?Button(key,content.clickLabel+" "+resource.displayName,click):Label(key,resource.displayName,22,54));
            }
            definitions.Add(Label("producer-heading",content.producersLabel,20,34));
            foreach(var producer in content.producers)
            {string id=producer.id;definitions.Add(Button("producer:"+id,producer.displayName,()=>buyProducer(id)));}
            definitions.Add(Label("upgrade-heading",content.upgradesLabel,20,34));
            foreach(var upgrade in content.upgrades)
            {string id=upgrade.id;definitions.Add(Button("upgrade:"+id,upgrade.displayName,()=>buyUpgrade(id)));}
            definitions.Add(Label("rule-heading",content.rulesLabel,20,34));
            foreach(var rule in content.rules)
            {string id=rule.id;definitions.Add(rule.triggerKind==Economy.RuleTriggerKind.Manual?Button("rule:"+id,rule.displayName,()=>triggerRule(id)):Label("rule:"+id,rule.displayName,18,70));}
            definitions.Add(Button("prestige","Prestige",doPrestige));
            definitions.Add(Button("save","Save game",save));definitions.Add(Button("load","Load saved game",load));
            definitions.Add(Label("status","Ready",17,48));
            var column=Ui.Column(definitions.ToArray());column.Spacing=9;column.Padding=new UiInsets(8);
            var declaration=Ui.Scroll(column);declaration.Id="economy-screen";declaration.ScrollbarVisibility=UiScrollbarVisibility.Hidden;
            declaration.Style.Background=UiColor.Clear;
            var canvas=new GameObject("CookieDemo UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvas.transform.SetParent(parent,false);canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1000,850);scaler.matchWidthOrHeight=.5f;
            var background=UnityUiView.Rect(canvas.transform,"Background");UnityUiView.Stretch(background);background.gameObject.AddComponent<Image>().color=new Color(.07f,.09f,.14f);
            view=UnityUiFactory.Mount(background,declaration,new UiContext(world, state,diagnostic:message=>Debug.LogWarning(message),presentation:tutorial));
            UnityUiView.Stretch(view.Root);view.Root.offsetMin=new Vector2(24,20);view.Root.offsetMax=new Vector2(-24,-20);
            if(UnityEngine.Object.FindObjectOfType<EventSystem>()==null)
            {var events=new GameObject("CookieDemo EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));events.transform.SetParent(parent,false);}
        }
        private TextTemplate Text(string key,string initial)
        {
            var value=state.Define(key,initial);texts.Add(key,value);
            return TextTemplate.Create("{value}",TextTemplate.Bind("value",UiBinding<string>.From(value)));
        }
        private TextDefinition Label(string key,string initial,int size,float height)
        {var d=Ui.Text(Text(key,initial));d.Id=key;d.Style.FontSize=size;d.Style.Color=new UiColor(.94f,.94f,.98f);d.Layout.MinHeight=height;d.Layout.Width=UiLength.Fill();return d;}
        private ButtonDefinition Button(string key,string initial,Action action)
        {
            var d=Ui.Button(Text(key,initial),UiAction.Callback(action));d.Id=key;
            var value=state.Define("enabled:"+key,true);enabled.Add(key,value);d.CanExecute=UiBinding<bool>.From(value);
            d.Style.Background=new UiColor(accent.r,accent.g,accent.b,accent.a);d.Style.FontSize=20;
            d.ContentPadding=new UiInsets(14,14,0,0);d.Layout.MinHeight=82;d.Layout.Width=UiLength.Fill();return d;
        }
        public void Resource(string id,string text){texts["resource:"+id].Current=text;}
        public void Producer(string id,string text,bool allowed){Set("producer:"+id,text,allowed);}
        public void Upgrade(string id,string text,bool allowed){Set("upgrade:"+id,text,allowed);}
        public void Rule(string id,string text,bool allowed){Set("rule:"+id,text,allowed);}
        public void Prestige(string text,bool allowed){Set("prestige",text,allowed);}
        public void Status(string text){texts["status"].Current=text;}
        public void Offline(string text){texts["offline"].Current=text;}
        private void Set(string key,string text,bool allowed)
        {using(state.Context.Batch()){texts[key].Current=text;if(enabled.TryGetValue(key,out var value))value.Current=allowed;}}
        public void Dispose(){view.Dispose();}
    }
}
