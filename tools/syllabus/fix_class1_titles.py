#!/usr/bin/env python3
"""Fixes Class 1 KL-SCERT chapter titles (as printed in the SCERT textbooks) and the Malayalam-medium Maths order.
Also sets each chapter's lesson title to the chapter title. Writes a rollback file first. One transaction.
  bash run_db.sh fix_class1_titles.py            # dry run
  bash run_db.sh fix_class1_titles.py --apply
"""
import json, os, sys, datetime
FIX = [["7dfc0cd9-fd4a-4307-96aa-6162630835e0", "ef8f3873-50af-4d83-a4d4-d5cd91a3b868", "പറവ പാറി", 1], ["1d51f6be-5515-49a4-9056-fc19abaf4512", "ab46854c-2d71-4639-860b-cacac7f2d44a", "മാനത്ത് പട്ടം", 3], ["8c62ddc1-b36f-49f0-9ba3-13982f0cbcec", "f8a22d11-8378-49b0-aa3a-e266fbe0d9d3", "പിറന്നാൾസമ്മാനം", 4], ["83226564-c1b2-44a1-a3ff-58e46abebdb7", "f5eba3c5-3ae5-4c2b-852d-17d4fc653513", "മണ്ണിലും മരത്തിലും", 5], ["21a33300-b53d-4917-8250-825ff3a6f464", "086e3c2e-1302-4936-b67d-d5e58976dec1", "പിന്നേം പിന്നേം ചെറുതായി പാലപ്പം", 6], ["76aee75a-819b-42b4-85e0-a5f34a1c3a71", "3fb0c215-85cf-4a31-a969-05890569bfa5", "പെയ്യട്ടങ്ങനെ പെയ്യട്ടെ", 7], ["ad686740-2c41-43ab-bead-608a7ffae92c", "6f525b9c-cf70-48d9-ae62-aff7dcc6bb08", "പച്ചക്കറിത്തോണി", 10], ["49a2f202-ad3b-48a7-a39f-5b716a4d5f14", "3b8930fc-991e-4202-8f6b-5b8aaccf9a13", "ആരുടെ കുപ്പായം", 13], ["b42cd711-ded4-4343-aab8-0ca8bf0b0ef3", "cc24880f-2ef8-4e25-b7a5-e7fd3c3b7f1e", "അദ്ഭുതപ്പൂത്തിരി", 15], ["c7a8b87b-3a11-4fab-84fc-6c9e4350f173", "52e936d9-8fe9-4ec1-b30c-6ef34d24e366", "ബലൂണുകളുടെ ഉത്സവം", 16], ["793a0444-e823-483b-8ee8-e0d4feccfc24", "9ac6d608-230e-4b4b-b7ca-1316d8802170", "പറവ പാറി", 1], ["7dd25ac7-bce6-4ab1-ac04-9364612d58d6", "462404bf-7f5b-4eaf-af15-886a962279c0", "മാനത്ത് പട്ടം", 3], ["4b927152-42f7-4cad-930e-b1b8aebabfbd", "05963617-7f72-46ed-96de-d65b33069eef", "പിറന്നാൾസമ്മാനം", 4], ["7d440b9b-da9d-4c0f-aaa1-03d0486940c1", "708fcefe-1433-4637-97ad-f297f14e163e", "മണ്ണിലും മരത്തിലും", 5], ["3b049883-d5e2-4885-8b6f-7709b765c36d", "4c78112a-7974-4c1d-a376-06e1d361e2bf", "പിന്നേം പിന്നേം ചെറുതായി പാലപ്പം", 6], ["c44b724c-31b6-43a0-86f0-a579eabe63bd", "42bfed12-4a22-4c63-b012-425db1f755c5", "പെയ്യട്ടങ്ങനെ പെയ്യട്ടെ", 7], ["a90e37c2-4dca-4eb1-907a-53bc9b509c06", "f4599969-5532-4e68-ada1-16360de4e64a", "പച്ചക്കറിത്തോണി", 10], ["fa3db91b-751e-482e-8522-d4e4732bbefe", "e113075d-913f-42b8-aa9e-f2c68b17fa47", "ആരുടെ കുപ്പായം", 13], ["660ce296-6cbd-4819-9e2c-9443fa06aaa5", "7ea9d752-8b19-4b62-a707-1cbd9542852e", "അദ്ഭുതപ്പൂത്തിരി", 15], ["a6d5dde0-20f7-4f6f-968b-035016be3c4a", "6bca01e3-2d24-4dd8-b328-6446652aafbb", "ബലൂണുകളുടെ ഉത്സവം", 16], ["7ce28b98-9caf-4a27-b4de-1ef77fd12543", "4412fdd5-1253-4cb0-8a97-a0b82344b7dc", "മാനം നോക്കി വരയ്ക്കാം", 1], ["b347e68b-6d51-4225-8091-0b3013e73214", "44f4b65f-0444-4874-a5e0-622fac85d588", "ചതുരക്കള്ളിയിൽ ഒരു വീട്", 2], ["ce1e148a-b8f6-406f-baa5-854287571ada", "c82d7d78-9135-45af-9bce-9c3a64daf612", "ക്ലിക്ക്... ക്ലിക്ക്...", 3], ["641f76ea-74ce-4ac0-a54d-c932b46e1aa0", "065ec5ad-c0d4-4064-9d4d-9ed7992104f5", "എത്രയെത്ര നിറങ്ങൾ!", 4], ["e5630fa0-1e43-4ee2-b603-e563fc2d6f59", "4831c6a6-473d-43f0-a226-5faf391d1dbf", "എണ്ണാം, നിറം കൊടുക്കാം...", 5], ["6fb342ee-402a-49cc-a832-24260479a80a", "5f9870ab-961f-4035-843f-4be58ddfe3b5", "ഇടത്താനേ... വലത്താനേ...", 6], ["b7e2b21d-3a3f-46dc-93f8-a3e27ce4029e", "29d68d05-dd36-4d58-be03-79f2b3d53026", "വാക്കുമഴ", 10], ["82392f0f-32dc-429e-a3fd-3618a1b8fe06", "6828d407-48b1-4c92-a23f-3981ae6550be", "കുത്തുകൾ എത്ര?", 11], ["07fdaf23-c242-4853-9c49-12cb744c91c6", "61b73ca8-02e7-44d5-b051-0ed0f7c97550", "ആകെ എത്ര?", 12], ["92463405-6776-49be-8b13-7645be471ca4", "4a483dd7-726c-4031-b3be-4ca44d6caf83", "ബാക്കി എത്ര?", 13], ["e69001b8-73d5-4b26-876e-4d54fc54f49a", "f6fd02a2-00d8-437e-a0e2-1202fb5ad18b", "പത്താകാൻ എത്ര?", 14], ["8485051f-9c20-4ac1-9f0b-851a89f00a42", "a0611f3b-1b08-4f41-9edc-ea4ee8567d4f", "നിഴൽ ആരുടെ?", 15], ["5ac42c5f-29c3-4884-acf8-33db5e1dcd6c", "170329a2-c46a-4b5d-9600-43c81989d0bf", "അലമാരയിലെ കൂട്ടുകാർ", 6], ["94494316-6097-4dc5-9468-693b45b1044e", "b627611a-2e8b-43b1-b679-bf5c40203092", "കൂടിയതും കുറഞ്ഞതും", 7], ["b91fc33d-977e-45b7-911c-385014a88eab", "ded5a3c2-fa5d-47f9-b615-c950071960c1", "നാണയക്കിലുക്കം", 8], ["1867bee1-7f43-40f7-a181-d091ef179296", "fcad6e74-e43d-4f3d-8efc-e1130ae376bf", "നേരമായി", 9], ["a76347b6-7b18-4c60-9b18-568178ff03b3", "305d4aa9-de88-4318-be84-0dff47cc6bbc", "പാവ വേണോ...പാവ", 10], ["12a057bd-b3c9-4dcf-831d-272a5691b60a", "fa16e514-32da-4fea-9ac1-d70893c3ecfc", "എണ്ണാം അടുക്കിവയ്ക്കാം", 11], ["fd7958d7-5cbf-4ec8-8055-b2a638172289", "a26bc29c-f8da-40f1-af97-df9492d5b8f5", "വരമേളം", 1], ["78cab25c-a5e4-43e5-8713-277e6b37c615", "63f0b2c6-1ed5-47c7-80a9-3f17fb2acd36", "തിത്തിരിക്കിളിയും ചങ്ങാതിമാരും", 2], ["c02604ba-93cb-4f84-9da4-c6ad9cf7a4a9", "c8d864cf-999f-4b78-8afa-9ae3c8d47412", "നടന്ന്... നടന്നങ്ങനെ", 3], ["6fc58c33-7cd5-4fcf-a9ce-d2a46ef115ff", "ba079e8c-29d0-48bd-a446-463053cabf14", "ആമിയുടെ പിറന്നാൾ", 4], ["1613ff0d-66ad-4f05-9d2e-489951e6b0d1", "9a1c6a26-6709-4dd5-a355-75338f7ac6cf", "പൂക്കാലം വന്നേ...", 5]]
def connect():
    import pymssql
    parts = dict(p.split("=", 1) for p in os.environ["CS"].split(";") if "=" in p)
    n = {k.strip().lower(): v.strip() for k, v in parts.items()}
    server = (n.get("server") or n.get("data source")).replace("tcp:", "").split(",")[0]
    return pymssql.connect(server=server, user=n.get("user id") or n.get("uid"), password=n.get("password") or n.get("pwd"),
                           database=n.get("initial catalog") or n.get("database"), as_dict=True)
cn = connect(); cur = cn.cursor()
ids = [f[0] for f in FIX]
cur.execute("SELECT Id, SubjectId, Title, Sequence FROM curriculum.Chapters WHERE Id IN (%s)" % ",".join(["%s"] * len(ids)), tuple(ids))
old = {str(r["Id"]).lower(): r for r in cur.fetchall()}
cur.execute("SELECT Id, Title FROM content.Lessons WHERE Id IN (%s)" % ",".join(["%s"] * len(FIX)), tuple(f[1] for f in FIX))
oldl = {str(r["Id"]).lower(): r["Title"] for r in cur.fetchall()}
assert len(old) == len(FIX) and len(oldl) == len(FIX), (len(old), len(oldl))
back = os.path.expanduser("~/class1_titles_rollback_%s.json" % datetime.datetime.now().strftime("%Y%m%d%H%M%S"))
json.dump([[f[0], old[f[0].lower()]["Title"], old[f[0].lower()]["Sequence"], f[1], oldl[f[1].lower()]] for f in FIX], open(back, "w"), ensure_ascii=False)
for f in FIX:
    o = old[f[0].lower()]
    print(f"{o['Sequence']:>2}->{f[3]:>2}  {o['Title']}  =>  {f[2]}")
if "--apply" not in sys.argv:
    print("dry run; rollback file", back); sys.exit()
now = datetime.datetime.utcnow()
subjects = {str(o["SubjectId"]) for o in old.values()}
for f in FIX:   # move out of the way first, in case (SubjectId, Sequence) is unique
    if old[f[0].lower()]["Sequence"] != f[3]:
        cur.execute("UPDATE curriculum.Chapters SET Sequence = Sequence + 1000 WHERE Id = %s", (f[0],))
for f in FIX:
    cur.execute("UPDATE curriculum.Chapters SET Title = %s, Sequence = %s, UpdatedAtUtc = %s WHERE Id = %s", (f[2], f[3], now, f[0]))
    cur.execute("UPDATE content.Lessons SET Title = %s, UpdatedAtUtc = %s WHERE Id = %s", (f[2], now, f[1]))
cn.commit()
print("APPLIED", len(FIX), "chapters; rollback file", back)
