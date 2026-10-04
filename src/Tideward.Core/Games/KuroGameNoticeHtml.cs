using System.Text.Json;

namespace Tideward.Core.Games;

public static class KuroGameNoticeHtml
{
    public static string Create(IReadOnlyList<KuroGameNotice> notices) => Template.Replace("__NOTICE_DATA__", JsonSerializer.Serialize(notices));

    private const string Template = """
        <!doctype html><html lang="zh-CN"><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <style>
        *{box-sizing:border-box}body{margin:0;background:#191b20;color:#e7e3d9;font:16px 'Segoe UI','Microsoft YaHei',sans-serif}
        header{height:64px;padding:0 24px;display:flex;align-items:center;border-bottom:1px solid #ffffff20;gap:16px}
        header strong{font-size:21px}header span{font-size:13px;color:#ada99f;flex:1}
        button{font:inherit;color:inherit;background:#ffffff10;border:1px solid #ffffff20;border-radius:6px;padding:8px 14px;cursor:pointer}
        button:focus-visible{outline:2px solid #e4c989;outline-offset:2px}
        main{display:grid;grid-template-columns:280px 1fr;height:calc(100vh - 64px)}nav{overflow:auto;padding:14px;border-right:1px solid #ffffff20}
        nav h2{font-size:14px;color:#b8ad93;margin:18px 8px 8px}nav button{display:block;width:100%;text-align:left;margin:6px 0;line-height:1.5}
        nav button[aria-current=true]{background:#dac58a25;border-color:#dac58a}article{overflow:auto;padding:26px 32px;line-height:1.8}
        article h1{font-size:22px;line-height:1.5;margin:0 0 24px}article img{max-width:100%;height:auto}a{color:#e4c989}
        @media(max-width:700px){main{grid-template-columns:210px 1fr}article{padding:18px}header{padding:0 16px}header span{display:none}}
        </style><header><strong>游戏公告</strong><span>库洛公开公告 · 连接角色后可查看角色公告与未读提醒</span><button id="close" aria-label="关闭游戏公告">关闭</button></header>
        <main><nav aria-label="公告列表"></nav><article><h1 id="title"></h1><div id="content"></div></article></main>
        <script>
        const notices=__NOTICE_DATA__;
        const send=(name,data)=>window.chrome?.webview?.postMessage({name,data});
        document.getElementById('close').onclick=()=>send('close_webview');
        document.addEventListener('keydown',e=>{if(e.key==='Escape')send('close_webview')});
        function select(item,button){
          document.querySelectorAll('nav button').forEach(b=>b.setAttribute('aria-current',String(b===button)));
          document.getElementById('title').textContent=item.Title;
          const parsed=new DOMParser().parseFromString(item.Content,'text/html');
          parsed.querySelectorAll('script,iframe,object,embed,form,link,meta,base,svg,math').forEach(e=>e.remove());
          parsed.querySelectorAll('*').forEach(e=>Array.from(e.attributes).forEach(a=>{
            const name=a.name.toLowerCase();
            if(name.startsWith('on')||name==='srcdoc'||name==='srcset')e.removeAttribute(a.name);
            if(name==='href'||name==='src'){
              try{if(new URL(a.value).protocol!=='https:')e.removeAttribute(a.name)}catch{e.removeAttribute(a.name)}
            }
          }));
          document.getElementById('content').replaceChildren(...parsed.body.childNodes);
          document.querySelector('article').scrollTop=0;
        }
        for(const [key,label]of [['game','公告'],['activity','资讯'],['recommend','推荐']]){
          const items=notices.filter(n=>n.Category===key);if(!items.length)continue;
          const title=document.createElement('h2');title.textContent=label;document.querySelector('nav').append(title);
          for(const item of items){const button=document.createElement('button');button.textContent=item.Title;button.onclick=()=>select(item,button);document.querySelector('nav').append(button)}
        }
        const first=document.querySelector('nav button');if(first)first.click();else document.getElementById('title').textContent='暂无公开公告，请稍后重试。';
        document.getElementById('content').onclick=e=>{const link=e.target.closest('a');if(link){e.preventDefault();send('new_openPage',{url:link.href,type:'browser'})}};
        </script></html>
        """;
}
